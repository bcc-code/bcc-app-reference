using System.Security.Cryptography;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Web.Tests;

public class BackchannelLogoutValidatorTests
{
    private const string Issuer = "https://idp.example/";
    private const string ClientId = "template";
    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    private readonly SigningCredentials _signing = new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256);
    private readonly BackchannelLogoutValidator _validator;

    public BackchannelLogoutValidatorTests()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_signing.Key);
        var options = new OpenIdConnectOptions
        {
            ClientId = ClientId,
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration),
        };
        _validator = new BackchannelLogoutValidator(new FixedOptions(options));
    }

    private string Token(Action<Dictionary<string, object>>? change = null, SigningCredentials? signedBy = null, string audience = ClientId, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["sid"] = "sid-1",
            ["events"] = new Dictionary<string, object> { [LogoutEvent] = new Dictionary<string, object>() },
        };
        change?.Invoke(claims);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(2),
            SigningCredentials = signedBy ?? _signing,
        });
    }

    [Fact]
    public async Task A_valid_logout_token_names_the_session_to_end()
    {
        var result = await _validator.ValidateAsync(Token(), CancellationToken.None);

        result.Logout.Should().Be(new BackchannelLogout(Issuer, "sid-1"));
    }

    [Fact]
    public async Task A_missing_token_is_refused() =>
        (await _validator.ValidateAsync(null, CancellationToken.None)).IsValid.Should().BeFalse();

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var other = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256);

        (await _validator.ValidateAsync(Token(signedBy: other), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_for_another_client_is_refused() =>
        (await _validator.ValidateAsync(Token(audience: "another-client"), CancellationToken.None)).IsValid.Should().BeFalse();

    [Fact]
    public async Task An_expired_token_is_refused() =>
        (await _validator.ValidateAsync(Token(expires: DateTime.UtcNow.AddMinutes(-10), change: c => c["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeSeconds()), CancellationToken.None))
            .IsValid.Should().BeFalse();

    [Fact]
    public async Task A_token_without_a_session_id_is_refused() =>
        (await _validator.ValidateAsync(Token(c => c.Remove("sid")), CancellationToken.None)).IsValid.Should().BeFalse();

    [Fact]
    public async Task A_token_without_the_logout_event_is_refused() =>
        (await _validator.ValidateAsync(Token(c => c["events"] = new Dictionary<string, object> { ["http://example/other"] = new Dictionary<string, object>() }), CancellationToken.None))
            .IsValid.Should().BeFalse();

    [Fact]
    public async Task A_token_with_a_nonce_is_refused_because_it_would_be_an_id_token() =>
        (await _validator.ValidateAsync(Token(c => c["nonce"] = "n"), CancellationToken.None)).IsValid.Should().BeFalse();

    private sealed class FixedOptions(OpenIdConnectOptions options) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue => options;

        public OpenIdConnectOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
    }
}
