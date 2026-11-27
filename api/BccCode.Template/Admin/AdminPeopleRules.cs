using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Admin;

/// <summary>
/// The person an admin endpoint acts on, loaded once (<see cref="AdminPeopleRules.LoadAsync"/>): the identities they signed in with,
/// the stored grant of the admin role (tracked, so a change is saved by the endpoint's transaction) and, when asked for, their sessions.
/// </summary>
/// <param name="PersonId">The person acted on.</param>
/// <param name="Role">The role <c>Auth:AdminRole</c> names; the endpoints are only reachable when it is set.</param>
/// <param name="Grant">The grant row of that role, active or revoked, or null when the person never had one.</param>
/// <param name="Sessions">The sessions of the person, found per identity (issuer and subject); empty unless loaded.</param>
public sealed record AdminTarget(Guid PersonId, string Role, RoleGrant? Grant, IReadOnlyList<AuthSessionRow> Sessions);

/// <summary>Decisions and loading shared by the endpoints that grant or revoke the admin role. <c>Permission</c> and <c>Validate</c> are pure.</summary>
public static class AdminPeopleRules
{
    public const string CannotRevokeSelfKey = "errors.admin.cannotRevokeSelf";
    public const string CannotRevokeSelfTitle = "You cannot take the administrator role from yourself.";

    /// <summary>The target, or null when no identity row has this person id (the person never signed in).</summary>
    public static async Task<AdminTarget?> LoadAsync(Guid personId, bool withSessions, TemplateDb db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var identities = await db.PersonIdentities.AsNoTracking().Where(i => i.PersonId == personId).ToListAsync(cancellationToken);
        var role = configuration[AuthSettings.AdminRoleKey];
        if (identities.Count == 0 || string.IsNullOrWhiteSpace(role)) return null;

        var grant = await db.RoleGrants.FirstOrDefaultAsync(g => g.PersonId == personId && g.Role == role, cancellationToken);
        List<AuthSessionRow> sessions = [];
        if (withSessions)
        {
            // A session is found by the issuer and subject of the identity it was created for (the pair back-channel logout uses too).
            foreach (var identity in identities)
                sessions.AddRange(await db.AuthSessions.Where(s => s.Issuer == identity.Issuer && s.Subject == identity.Subject).ToListAsync(cancellationToken));
        }

        return new AdminTarget(personId, role, grant, sessions);
    }

    /// <summary>404 when nobody with this id has signed in (the admin page only shows people who have), else null.</summary>
    public static NotFound? Permission(AdminTarget? target)
    {
        return target is null ? TypedResults.NotFound() : null;
    }
}
