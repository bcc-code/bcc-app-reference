using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Features.CurrentUser;

/// <summary>What the SPA needs to greet the user and hide actions. Not the claim list, and no subject.</summary>
public sealed record MeResponse(string DisplayName, IReadOnlyList<string> Roles);

public static class GetMeEndpoint
{
    [WolverineGet("/api/me")]
    public static MeResponse Get(ClaimsPrincipal user) => new(user.DisplayName(), user.Roles());
}
