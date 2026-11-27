using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>A stand-in Entra tenant: fixed OIDC configuration, so the sign-in redirect needs no metadata fetch.</summary>
public static class TestEntra
{
    public const string TenantId = "11111111-1111-4111-8111-111111111111";
    public const string ClientId = "template-entra";
    public const string Issuer = "https://entra.invalid/" + TenantId + "/v2.0";
    public const string AuthorizationEndpoint = "https://entra.invalid/authorize";
    public const string TokenEndpoint = "https://entra.invalid/token";
    public const string EndSessionEndpoint = "https://entra.invalid/logout";

    public static OpenIdConnectConfiguration Configuration()
    {
        return new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            EndSessionEndpoint = EndSessionEndpoint,
        };
    }
}
