using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.Api.Auth;

/// <summary>What a validated ID token says about the person who signed in.</summary>
/// <param name="Provider">One of <see cref="AuthProviders"/>.</param>
/// <param name="Issuer">The <c>iss</c> that goes into the session, which the session store and a revoke find sessions with.</param>
/// <param name="Subject">The provider's <c>sub</c>.</param>
/// <param name="DisplayName">The <c>name</c> claim, if any.</param>
/// <param name="SignonPersonUid">BCC Signon's person id, which becomes the person id; null for Entra, which gets one issued.</param>
/// <param name="BootstrapId">What <c>Auth:BootstrapAdmins</c> is matched against: the Signon <c>personUid</c> or the Entra <c>oid</c>.</param>
public sealed record SignInRequest(string Provider, string Issuer, string Subject, string? DisplayName, Guid? SignonPersonUid, string? BootstrapId);

/// <param name="PersonId">The id that keys the person's data.</param>
/// <param name="Roles">The roles stored for the person, ready to become <c>role</c> claims.</param>
public sealed record SignedInPerson(Guid PersonId, IReadOnlyList<string> Roles);

/// <summary>
/// Records a sign-in (ADR 0031), called from each scheme's token validation: keeps the identity row of (provider, subject) and so
/// the person id, applies the bootstrap list, and returns the roles stored for the person. It never logs a person id or a name.
/// </summary>
public sealed class SignInPeople(IDbContextFactory<TemplateDb> factory, TimeProvider time, IConfiguration configuration)
{
    // The first sign-in of a person can arrive twice at once (two tabs); every loser of the insert reloads and writes again as an
    // update, which no other writer can fail, so a few attempts are plenty.
    private const int MAX_ATTEMPTS = 3;

    private const string UNIQUE_VIOLATION = "23505";

    public async Task<SignedInPerson> RecordAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        var personId = await UpsertIdentityAsync(request, cancellationToken);
        var adminRole = configuration[AdminMfaMiddleware.AdminRoleKey];
        // An empty setting names no role: nobody is granted or bootstrapped (standard 15.5).
        if (string.IsNullOrWhiteSpace(adminRole)) return new SignedInPerson(personId, []);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (IsBootstrapAdmin(request))
            await BootstrapAsync(db, personId, adminRole, cancellationToken);

        var roles = await db.RoleGrants.AsNoTracking().Where(g => g.PersonId == personId && g.RevokedAt == null).Select(g => g.Role)
            .ToListAsync(cancellationToken);
        return new SignedInPerson(personId, roles);
    }

    private async Task<Guid> UpsertIdentityAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var now = time.GetUtcNow();
            var identity = await db.PersonIdentities.FirstOrDefaultAsync(i => i.Provider == request.Provider && i.Subject == request.Subject, cancellationToken);
            if (identity is null)
            {
                identity = PersonIdentity.Create(request.Provider, request.Subject, request.SignonPersonUid ?? Guid.CreateVersion7(now), request.Issuer, request.DisplayName, now);
                db.PersonIdentities.Add(identity);
            }
            else
            {
                identity.RecordSignIn(request.DisplayName, request.Issuer, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                // BCC Signon's person uid is the person id, so data keyed by it keeps its owner whatever an older row says.
                return request.SignonPersonUid ?? identity.PersonId;
            }
            catch (DbUpdateException ex) when (attempt < MAX_ATTEMPTS && IsUniqueViolation(ex))
            {
                // Reload and write again: the loser of a first sign-in race finds the winner's row, and so the same person id.
            }
        }
    }

    private bool IsBootstrapAdmin(SignInRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BootstrapId)) return false;
        return BootstrapAdmins.Parse(configuration[BootstrapAdmins.Key]).Any(a => string.Equals(a.Provider, request.Provider, StringComparison.Ordinal)
            && string.Equals(a.Id, request.BootstrapId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Grants the admin role once, to a person who has never had a grant row for it, active or revoked. After that the row is the truth:
    /// a revoke sticks while the person stays in the list.
    /// </summary>
    private async Task BootstrapAsync(TemplateDb db, Guid personId, string adminRole, CancellationToken cancellationToken)
    {
        if (await db.RoleGrants.AnyAsync(g => g.PersonId == personId && g.Role == adminRole, cancellationToken)) return;

        db.RoleGrants.Add(RoleGrant.Grant(personId, adminRole, time.GetUtcNow(), grantedBy: null));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A sign-in at the same moment inserted the row first; its state is the one to keep.
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: UNIQUE_VIOLATION };
    }
}
