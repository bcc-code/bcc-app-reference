using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Web.Auth;

public sealed record BackchannelLogout(string Issuer, string Sid);

/// <summary>Result of validating a logout token. <see cref="Error"/> is for logs only; it is never sent to the caller.</summary>
public sealed record BackchannelLogoutResult(BackchannelLogout? Logout, string? Error)
{
    public bool IsValid => Logout is not null;
}

/// <summary>
/// Validates the logout token an identity provider posts for OIDC Back-Channel Logout 1.0 (section 2.6): signature,
/// issuer, audience (our client id), lifetime, <c>iat</c>, <c>sid</c>, the <c>events</c> member, and no <c>nonce</c>.
/// A token without <c>sid</c> cannot be acted on, because sessions are found by issuer and <c>sid</c>.
/// </summary>
public sealed class BackchannelLogoutValidator(IOptionsMonitor<OpenIdConnectOptions> oidcOptions)
{
    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    public async Task<BackchannelLogoutResult> ValidateAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return Fail("logout_token is missing");

        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await options.ConfigurationManager!.GetConfigurationAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("the OIDC discovery document could not be read");
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(rawToken, new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
            ValidTypes = null,   // Auth0 sends typ "JWT", not "logout+jwt"; explicit typing is a recommendation, not a requirement
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) return Fail("logout_token failed validation");

        if (!token.TryGetPayloadValue<long>("iat", out _)) return Fail("logout_token has no iat");
        if (!token.TryGetPayloadValue<string>("sid", out var sid) || string.IsNullOrEmpty(sid)) return Fail("logout_token has no sid");
        if (token.TryGetPayloadValue<string>("nonce", out _)) return Fail("logout_token has a nonce");
        if (!token.TryGetClaim("events", out var events) || !HasLogoutEvent(events.Value)) return Fail("logout_token has no backchannel-logout event");

        return new BackchannelLogoutResult(new BackchannelLogout(token.Issuer, sid), null);
    }

    private static bool HasLogoutEvent(string eventsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(eventsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(LogoutEvent, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static BackchannelLogoutResult Fail(string error) => new(null, error);
}
