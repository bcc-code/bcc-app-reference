using System.Text.Json;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Api.Auth;

/// <summary>The sessions a validated logout token ends: those of one identity-provider session (<paramref name="Sid"/>), or, without one, of the subject.</summary>
public sealed record BackchannelLogout(string Issuer, string? Sid, string? Subject);

/// <summary>Result of validating a logout token. <see cref="Error"/> is for logs only; it is never sent to the caller.</summary>
public sealed record BackchannelLogoutResult(BackchannelLogout? Logout, string? Error)
{
    public bool IsValid => Logout is not null;
}

/// <summary>
/// Validates the logout token an identity provider posts for OIDC Back-Channel Logout 1.0 (section 2.6): signature, issuer,
/// audience (our client id), lifetime, <c>iat</c> (fresh, so an old token is a replay), a <c>sid</c> or a <c>sub</c>, the
/// <c>events</c> member, and no <c>nonce</c>. The signing keys, issuer and audience are the ones the sign-in handler uses, so no
/// second configuration can drift from it.
/// </summary>
public sealed class BackchannelLogoutValidator(IOptionsMonitor<OpenIdConnectOptions> oidcOptions, TimeProvider time)
{
    private const string LOGOUT_EVENT = "http://schemas.openid.net/event/backchannel-logout";

    // The identity provider sends the token the moment the session ends, so an old one is a replay.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public async Task<BackchannelLogoutResult> ValidateAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return Fail("logout_token is missing");

        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        if (options.ConfigurationManager is null || string.IsNullOrEmpty(options.ClientId)) return Fail("OIDC is not configured");

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("the OIDC discovery document could not be read");
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(rawToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = options.ClientId,
            ValidateLifetime = true,
            // Our own validator so the clock is the injected one. exp is required for a logout token.
            LifetimeValidator = (notBefore, expires, _, _) => IsWithinLifetime(notBefore, expires),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidTypes = null,   // Auth0 sends typ "JWT", not "logout+jwt"; explicit typing is a recommendation, not a requirement
        });
        if (result.Exception is SecurityTokenInvalidLifetimeException) return Fail("logout_token is expired or not yet valid");
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) return Fail("logout_token failed validation");

        if (!token.TryGetPayloadValue<long>(JwtRegisteredClaimNames.Iat, out var iat)) return Fail("logout_token has no iat");
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
        if (time.GetUtcNow() - issuedAt > MaxAge || issuedAt - time.GetUtcNow() > ClockSkew) return Fail("logout_token iat is not fresh");
        if (token.TryGetPayloadValue<string>(JwtRegisteredClaimNames.Nonce, out _)) return Fail("logout_token has a nonce");
        if (!HasLogoutEvent(token)) return Fail("logout_token has no backchannel-logout event");

        var sid = Claim(token, AuthClaims.SessionId);
        var subject = Claim(token, AuthClaims.Subject);
        if (sid is null && subject is null) return Fail("logout_token needs a sid or a sub");
        return new BackchannelLogoutResult(new BackchannelLogout(token.Issuer, sid, subject), null);
    }

    private bool IsWithinLifetime(DateTime? notBefore, DateTime? expires)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return expires is { } exp && exp >= now - ClockSkew && (notBefore is not { } nbf || nbf <= now + ClockSkew);
    }

    private static bool HasLogoutEvent(JsonWebToken token)
    {
        return token.TryGetPayloadValue<JsonElement>("events", out var events)
            && events.ValueKind == JsonValueKind.Object
            && events.TryGetProperty(LOGOUT_EVENT, out _);
    }

    private static string? Claim(JsonWebToken token, string type)
    {
        return token.TryGetPayloadValue<string>(type, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    private static BackchannelLogoutResult Fail(string error)
    {
        return new(null, error);
    }
}
