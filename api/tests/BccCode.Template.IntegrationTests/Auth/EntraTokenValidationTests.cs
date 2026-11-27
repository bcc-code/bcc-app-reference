using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>
/// What the Entra scheme does with a validated ID token (ADR 0031), driven through the event handler the host registered on the
/// scheme, in a host that has Entra on, against the real database. A callback with a real authorization code needs an identity
/// provider; the event is what runs after the token is validated, so this covers the decision that matters: a token from another
/// tenant gets no session and leaves no trace.
/// </summary>
public class EntraTokenValidationTests(AppFixture fixture) : AppTest(fixture)
{
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "token-test-key" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JwtSecurityToken Token(string? tenant, string subject = "entra-sub-tenant-test", string? oid = null, params string[] amr)
    {
        List<Claim> claims = [new("sub", subject), new("oid", oid ?? Guid.NewGuid().ToString()), new("name", "Tina Tenant")];
        claims.AddRange((amr.Length > 0 ? amr : ["pwd", "mfa"]).Select(method => new Claim("amr", method)));
        if (tenant is not null) claims.Add(new Claim("tid", tenant));
        return new JwtSecurityToken(TestEntra.Issuer, TestEntra.ClientId, claims, signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));
    }

    // The handler of the scheme as the host registered it, called with the context the OIDC handler would pass.
    private static async Task<TokenValidatedContext> ValidateAsync(IAlbaHost host, JwtSecurityToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(EnabledProviders.EntraScheme);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme(EnabledProviders.EntraScheme, null, typeof(OpenIdConnectHandler));
        // The principal of the real handler holds the claims of the ID token (amr included) except the ones its default claim actions delete.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(token.Claims.Where(c => c.Type is not ("iss" or "aud" or "exp" or "nbf" or "iat")), "oidc", "name", "role"));
        var context = new TokenValidatedContext(http, scheme, options, principal, new AuthenticationProperties()) { SecurityToken = token };

        await options.Events.TokenValidated(context);
        return context;
    }

    private async Task<int> IdentityCountAsync()
    {
        await using var db = Fixture.NewDb();
        return await db.PersonIdentities.CountAsync(Ct);
    }

    [Theory]
    [InlineData("22222222-2222-4222-8222-222222222222")]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_token_from_another_tenant_or_without_one_gets_no_session_and_writes_no_identity(string? tenant)
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var context = await ValidateAsync(host, Token(tenant));

        context.Result?.Failure.Should().NotBeNull("the sign-in is refused");
        context.Result!.Succeeded.Should().BeFalse();
        (await IdentityCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_token_from_the_configured_tenant_is_recorded_and_gets_a_person_id()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var context = await ValidateAsync(host, Token(TestEntra.TenantId.ToUpperInvariant()));

        context.Result.Should().BeNull("the sign-in goes on to write the session");
        context.Principal!.HasClaim(c => c.Type == AuthClaims.PersonId).Should().BeTrue();
        context.Principal.FindFirst(AuthClaims.Provider)!.Value.Should().Be(AuthProviders.Entra);
        (await IdentityCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_bootstrapped_entra_admin_without_mfa_in_amr_is_sent_to_the_second_factor_page_with_no_session()
    {
        var oid = Guid.NewGuid().ToString();
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true, new Dictionary<string, string?> { ["Auth:BootstrapAdmins"] = $"entra:{oid}" });

        var context = await ValidateAsync(host, Token(TestEntra.TenantId, oid: oid, amr: "pwd"));

        context.Result!.Handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/auth/mfa-required");
        context.Principal!.HasClaim(c => c.Type == AuthClaims.PersonId).Should().BeFalse("no session principal was set");
    }

    [Fact]
    public async Task A_bootstrapped_entra_admin_with_mfa_in_amr_signs_in_as_an_administrator()
    {
        var oid = Guid.NewGuid().ToString();
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true, new Dictionary<string, string?> { ["Auth:BootstrapAdmins"] = $"entra:{oid}" });

        var context = await ValidateAsync(host, Token(TestEntra.TenantId, oid: oid, amr: ["pwd", "mfa"]));

        context.Result.Should().BeNull();
        context.Principal!.IsInRole("admin").Should().BeTrue();
        context.Principal.HasClaim(AuthClaims.Mfa, "true").Should().BeTrue();
    }

    [Fact]
    public async Task An_entra_member_without_mfa_in_amr_signs_in_because_only_administrators_need_it()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var context = await ValidateAsync(host, Token(TestEntra.TenantId, amr: "pwd"));

        context.Result.Should().BeNull();
        context.Principal!.IsInRole("admin").Should().BeFalse();
    }
}
