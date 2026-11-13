using System.Security.Cryptography;
using BccCode.Template.Api.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Tests;

public class BackchannelLogoutValidatorTests
{
    private const string ISSUER = "https://idp.example/";
    private const string CLIENT_ID = "template";
    private const string LOGOUT_EVENT = "http://schemas.openid.net/event/backchannel-logout";

    private readonly SigningCredentials _signing = new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256);
    // Starts at the real time: the token library stamps nbf and iat of a token it makes with the real clock.
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly BackchannelLogoutValidator _validator;

    public BackchannelLogoutValidatorTests()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = ISSUER };
        configuration.SigningKeys.Add(_signing.Key);
        var options = new OpenIdConnectOptions
        {
            ClientId = CLIENT_ID,
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration),
        };
        _validator = new BackchannelLogoutValidator(new FixedOptions(options), _time);
    }

    private string Token(Action<Dictionary<string, object>>? change = null, SigningCredentials? signedBy = null, string audience = CLIENT_ID, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["iat"] = _time.GetUtcNow().ToUnixTimeSeconds(),
            ["sid"] = "sid-1",
            ["events"] = new Dictionary<string, object> { [LOGOUT_EVENT] = new Dictionary<string, object>() },
        };
        change?.Invoke(claims);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ISSUER,
            Audience = audience,
            Claims = claims,
            Expires = expires ?? _time.GetUtcNow().UtcDateTime.AddMinutes(2),
            SigningCredentials = signedBy ?? _signing,
        });
    }

    [Fact]
    public async Task A_valid_logout_token_names_the_session_to_end()
    {
        var result = await _validator.ValidateAsync(Token(), CancellationToken.None);

        result.Logout.Should().Be(new BackchannelLogout(ISSUER, "sid-1", null));
    }

    [Fact]
    public async Task A_missing_token_is_refused()
    {
        (await _validator.ValidateAsync(null, CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var other = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256);

        (await _validator.ValidateAsync(Token(signedBy: other), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_for_another_client_is_refused()
    {
        (await _validator.ValidateAsync(Token(audience: "another-client"), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        (await _validator.ValidateAsync(Token(expires: _time.GetUtcNow().UtcDateTime.AddMinutes(-10)), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_without_a_session_id_names_the_subject_instead()
    {
        var result = await _validator.ValidateAsync(Token(c => { c.Remove("sid"); c["sub"] = "subject-1"; }), CancellationToken.None);

        result.Logout.Should().Be(new BackchannelLogout(ISSUER, null, "subject-1"));
    }

    [Fact]
    public async Task A_token_with_neither_a_session_id_nor_a_subject_is_refused()
    {
        (await _validator.ValidateAsync(Token(c => c.Remove("sid")), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_issued_long_ago_is_a_replay_and_is_refused()
    {
        var stale = Token(c => c["iat"] = _time.GetUtcNow().AddMinutes(-30).ToUnixTimeSeconds(), expires: _time.GetUtcNow().UtcDateTime.AddMinutes(30));

        (await _validator.ValidateAsync(stale, CancellationToken.None)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_without_the_logout_event_is_refused()
    {
        (await _validator.ValidateAsync(Token(c => c["events"] = new Dictionary<string, object> { ["http://example/other"] = new Dictionary<string, object>() }), CancellationToken.None))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_with_a_nonce_is_refused_because_it_would_be_an_id_token()
    {
        (await _validator.ValidateAsync(Token(c => c["nonce"] = "n"), CancellationToken.None)).IsValid.Should().BeFalse();
    }

    private sealed class FixedOptions(OpenIdConnectOptions options) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue => options;

        public OpenIdConnectOptions Get(string? name)
        {
            return options;
        }

        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener)
        {
            return null;
        }
    }
}
