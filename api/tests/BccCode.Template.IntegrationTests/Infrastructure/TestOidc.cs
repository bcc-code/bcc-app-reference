using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>A stand-in identity provider: fixed OIDC configuration (no metadata fetch) and a key to sign logout tokens with.</summary>
public static class TestOidc
{
    public const string Issuer = "https://oidc.invalid/";
    public const string ClientId = "template";
    public const string AuthorizationEndpoint = "https://oidc.invalid/authorize";
    public const string TokenEndpoint = "https://oidc.invalid/token";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static OpenIdConnectConfiguration Configuration()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            EndSessionEndpoint = "https://oidc.invalid/logout",
        };
        configuration.SigningKeys.Add(SigningKey);
        return configuration;
    }

    public static string LogoutToken(string? sid = "sid-1", string? subject = "sub-1", string audience = ClientId, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() },
            ["jti"] = Guid.NewGuid().ToString(),
        };
        if (sid is not null) claims["sid"] = sid;
        if (subject is not null) claims["sub"] = subject;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(2),
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
