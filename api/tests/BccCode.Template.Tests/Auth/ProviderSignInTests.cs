using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Security;

namespace BccCode.Template.Tests.Auth;

/// <summary>The principal each provider's ID token turns into, and the Entra decision on a second factor (ADR 0031).</summary>
public sealed class ProviderSignInTests
{
    private const string ISSUER = "https://idp.example/";
    private const string SIGNON_UID = "a0000000-0000-4000-8000-000000000001";
    private const string SOMEONE_ELSE = "b0000000-0000-4000-8000-000000000002";

    private static ClaimsPrincipal Token(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc", AuthClaims.Name, AuthClaims.Role));
    }

    private static IEnumerable<string> ValuesOf(ClaimsPrincipal principal, string type)
    {
        return principal.FindAll(type).Select(c => c.Value);
    }

    [Fact]
    public void A_signon_token_becomes_the_slim_claims_with_the_provider_and_its_roles_kept()
    {
        var token = Token(new("sub", "s"), new(AuthClaims.SignonPersonUid, SIGNON_UID), new("name", "N"), new("sid", "sid-1"), new("role", "admin"),
            new("email", "x@example.test"));

        var principal = SessionClaims.FromIdToken(token, ISSUER, AuthProviders.BccSignon)!;

        principal.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", "name", "sid", "role", "iss", AuthClaims.Provider);
        ValuesOf(principal, AuthClaims.Provider).Should().Equal("bcc-signon");
        principal.IsInRole("admin").Should().BeTrue("the Signon role claim still grants the role");
        SessionClaims.SignonPersonUid(token).Should().Be(Guid.Parse(SIGNON_UID));
    }

    [Fact]
    public void A_signon_token_drops_claims_the_app_sets_itself()
    {
        var token = Token(new("sub", "s"), new(AuthClaims.SignonPersonUid, SIGNON_UID), new(AuthClaims.PersonId, SOMEONE_ELSE), new(AuthClaims.Provider, "entra"),
            new(AuthClaims.Mfa, "true"));

        var principal = SessionClaims.FromIdToken(token, ISSUER, AuthProviders.BccSignon)!;

        principal.HasClaim(c => c.Type == AuthClaims.PersonId).Should().BeFalse("the person id is added afterwards from the database or the personUid");
        principal.HasClaim(c => c.Type == AuthClaims.Mfa).Should().BeFalse();
        ValuesOf(principal, AuthClaims.Provider).Should().Equal("bcc-signon");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public void A_signon_token_needs_a_person_uid_that_is_a_guid(string? personUid)
    {
        Claim[] claims = personUid is null ? [new("sub", "s")] : [new("sub", "s"), new(AuthClaims.SignonPersonUid, personUid)];

        SessionClaims.FromIdToken(Token(claims), ISSUER, AuthProviders.BccSignon).Should().BeNull();
    }

    [Fact]
    public void An_entra_token_needs_a_subject_and_no_person_uid()
    {
        SessionClaims.FromIdToken(Token(new Claim("sub", "entra-sub")), ISSUER, AuthProviders.Entra).Should().NotBeNull();
        SessionClaims.FromIdToken(Token(new Claim("name", "N")), ISSUER, AuthProviders.Entra).Should().BeNull();
        SessionClaims.FromIdToken(Token(new Claim("sub", "")), ISSUER, AuthProviders.Entra).Should().BeNull();
    }

    [Fact]
    public void An_entra_token_drops_person_provider_mfa_and_role_claims()
    {
        var token = Token(new("sub", "entra-sub"), new("name", "N"), new("sid", "sid-1"), new(AuthClaims.PersonId, SOMEONE_ELSE), new(AuthClaims.Provider, "bcc-signon"),
            new(AuthClaims.Mfa, "true"), new("role", "admin"), new("oid", "o"), new("tid", "t"), new("amr", "pwd"));

        var principal = SessionClaims.FromIdToken(token, ISSUER, AuthProviders.Entra)!;

        principal.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", "name", "sid", "iss", AuthClaims.Provider);
        ValuesOf(principal, AuthClaims.Provider).Should().Equal("entra");
        principal.IsInRole("admin").Should().BeFalse("Entra's roles are not read; the stored grant adds the role");
    }

    [Fact]
    public void An_entra_token_sets_mfa_from_amr_only()
    {
        SessionClaims.FromIdToken(Token(new("sub", "s"), new("amr", "pwd"), new("amr", "mfa")), ISSUER, AuthProviders.Entra)!
            .HasClaim(AuthClaims.Mfa, "true").Should().BeTrue();
        SessionClaims.FromIdToken(Token(new("sub", "s"), new("amr", "pwd")), ISSUER, AuthProviders.Entra)!
            .HasClaim(c => c.Type == AuthClaims.Mfa).Should().BeFalse();
    }

    [Fact]
    public void The_issuer_of_the_token_is_kept_or_added()
    {
        ValuesOf(SessionClaims.FromIdToken(Token(new Claim("sub", "s")), ISSUER, AuthProviders.Entra)!, "iss").Should().Equal(ISSUER);
        ValuesOf(SessionClaims.FromIdToken(Token(new("sub", "s"), new("iss", "https://own.example/")), ISSUER, AuthProviders.Entra)!, "iss")
            .Should().Equal("https://own.example/");
    }

    [Fact]
    public void The_person_id_and_the_stored_roles_are_added_once()
    {
        var principal = SessionClaims.FromIdToken(Token(new("sub", "s"), new(AuthClaims.SignonPersonUid, SIGNON_UID), new("role", "admin")), ISSUER, AuthProviders.BccSignon)!;

        SessionClaims.AddPerson(principal, Guid.Parse(SIGNON_UID), ["admin", "editor"]);

        ValuesOf(principal, AuthClaims.PersonId).Should().Equal(SIGNON_UID);
        ValuesOf(principal, "role").Should().BeEquivalentTo("admin", "editor");
        principal.TryPersonId(out var personId).Should().BeTrue();
        personId.Should().Be(Guid.Parse(SIGNON_UID));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void An_entra_administrator_without_mfa_gets_no_session(bool isAdmin, bool mfa, bool refused)
    {
        var claims = new List<Claim>();
        if (isAdmin) claims.Add(new Claim(AuthClaims.Role, "admin"));
        if (mfa) claims.Add(new Claim(AuthClaims.Mfa, "true"));

        ProviderSignIn.RefusesAdminWithoutMfa(Token([.. claims]), "admin").Should().Be(refused);
    }

    [Fact]
    public void With_no_admin_role_configured_no_entra_sign_in_is_refused_for_it()
    {
        ProviderSignIn.RefusesAdminWithoutMfa(Token(new Claim(AuthClaims.Role, "admin")), "").Should().BeFalse();
    }

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", "11111111-1111-4111-8111-111111111111", true)]
    [InlineData("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", false)]
    [InlineData(null, "11111111-1111-4111-8111-111111111111", false)]
    [InlineData("", "11111111-1111-4111-8111-111111111111", false)]
    [InlineData("11111111-1111-4111-8111-111111111111", null, false)]
    public void The_token_tenant_must_be_the_configured_one(string? token, string? configured, bool expected)
    {
        ProviderSignIn.TenantMatches(token, configured).Should().Be(expected);
    }
}
