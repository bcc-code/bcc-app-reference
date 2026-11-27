using System.Security.Claims;

namespace BccCode.Template.Security;

/// <summary>
/// What the signed-in caller is: their person id and the roles in their token. An endpoint takes it as a parameter and
/// <c>CallerAccessParameterStrategy</c> calls <see cref="Load"/> once per request, however many methods ask for it; never call
/// it from an endpoint. The template has no other source of access (no Core API); the stored admin grants arrive as roles in the session. An app that gets one adds
/// its data here, in <c>LoadAsync</c>, so <c>Permission</c> stays a pure function of it.
/// </summary>
public sealed record CallerAccess(Guid PersonId, IReadOnlySet<string> Roles)
{
    /// <summary>The access of the signed-in caller. The fallback policy guarantees the person id claim.</summary>
    public static CallerAccess Load(ClaimsPrincipal user)
    {
        return new CallerAccess(user.PersonId(), user.Roles().ToHashSet(StringComparer.Ordinal));
    }

    public bool Has(string role)
    {
        return Roles.Contains(role);
    }
}
