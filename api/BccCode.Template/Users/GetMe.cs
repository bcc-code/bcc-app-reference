using System.Security.Claims;
using BccCode.Template.Security;

namespace BccCode.Template.Users;

// What the SPA needs to greet the user and hide actions. Not the claim list, and no person uid.
public sealed record GetMeResponse(string DisplayName, IReadOnlyList<string> Roles);

public static class GetMeEndpoint
{
    [WolverineGet("/api/me")]
    public static GetMeResponse Get(ClaimsPrincipal user, CallerAccess access)
    {
        return new GetMeResponse(user.DisplayName(), [.. access.Roles.Order(StringComparer.Ordinal)]);
    }
}
