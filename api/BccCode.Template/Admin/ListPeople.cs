using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace BccCode.Template.Admin;

/// <param name="PersonId">The app's person id, which the grant and revoke routes take.</param>
/// <param name="DisplayName">The name from the last sign-in; empty when the token had none.</param>
/// <param name="Provider">The provider key of this identity (<c>bcc-signon</c> or <c>entra</c>).</param>
/// <param name="LastSignInAt">When this identity last signed in.</param>
/// <param name="IsAdmin">Whether the person has an active stored grant of the admin role.</param>
public sealed record PersonItem(Guid PersonId, string DisplayName, string Provider, DateTimeOffset LastSignInAt, bool IsAdmin);

public sealed record ListPeopleResponse(IReadOnlyList<PersonItem> People);

/// <summary>
/// The people who have signed in, one entry per identity (a person who used both providers shows twice, because they are two people
/// in the app, ADR 0031). <c>IsAdmin</c> is the stored grant only: an administrator who has the role from BCC Signon's token alone has
/// no grant until someone gives one. Under <c>/api/admin</c>, so the admin policy applies by convention.
/// </summary>
public static class ListPeopleEndpoint
{
    public const int MaxResults = 100;

    public static async Task<IReadOnlyList<PersonItem>> LoadAsync(string? search, TemplateDb db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var role = configuration[AuthSettings.AdminRoleKey];
        var identities = db.PersonIdentities.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            identities = identities.Where(i => EF.Functions.ILike(i.DisplayName, pattern, "\\"));
        }

        return await identities
            .OrderBy(i => i.DisplayName).ThenBy(i => i.Provider).ThenBy(i => i.Subject)
            .Take(MaxResults)
            .Select(i => new PersonItem(
                i.PersonId, i.DisplayName, i.Provider, i.LastSignInAt,
                !string.IsNullOrWhiteSpace(role) && db.RoleGrants.Any(g => g.PersonId == i.PersonId && g.Role == role && g.RevokedAt == null)))
            .ToListAsync(cancellationToken);
    }

    [WolverineGet("/api/admin/people")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static ListPeopleResponse Get([NotBody] IReadOnlyList<PersonItem> people)
    {
        return new ListPeopleResponse(people);
    }

    // The search is a contains match, not a pattern: % and _ and the escape character itself match as themselves.
    private static string EscapeLike(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    }
}
