using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Api.Configuration;
using BccCode.Template.Api.Frontend;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template.Tests;

public class ProblemsTests
{
    [Fact]
    public void A_problem_has_a_stable_urn_type_and_the_key_as_an_extension()
    {
        var problem = Problems.Conflict();

        problem.Status.Should().Be(409);
        problem.Type.Should().Be("urn:bcc:template:errors.conflict");
        problem.Extensions["key"].Should().Be("errors.conflict");
    }

    [Fact]
    public void A_problem_never_carries_detail()
    {
        Problems.Conflict().Detail.Should().BeNull();
    }

    [Theory]
    [InlineData(401, "errors.unauthorized")]
    [InlineData(404, "errors.notFound")]
    [InlineData(429, "errors.tooManyRequests")]
    [InlineData(500, "errors.unexpected")]
    public void Problems_written_by_the_framework_get_a_type_and_key_too(int status, string key)
    {
        var context = new ProblemDetailsContext { HttpContext = new DefaultHttpContext(), ProblemDetails = new ProblemDetails { Status = status } };

        Problems.Customize(context);

        context.ProblemDetails.Type.Should().Be(Problems.TypePrefix + key);
        context.ProblemDetails.Extensions["key"].Should().Be(key);
    }
}

public class EndpointConventionTests
{
    [Theory]
    [InlineData("/api/admin/stats", "Admin")]
    [InlineData("/api/admin", "Admin")]
    [InlineData("/API/Admin/stats", "Admin")]
    [InlineData("/api/notes", "")]
    [InlineData("/api/administrators", "")]
    [InlineData("/api/me", "")]
    public void The_policy_follows_from_the_route(string route, string policy)
    {
        EndpointConventions.ExpectedPolicy(route).Should().Be(policy);
    }

    [Fact]
    public void The_operation_id_is_the_class_name_without_Endpoint()
    {
        OpenApiNaming.OperationId(typeof(BccCode.Template.Notes.CreateNoteEndpoint)).Should().Be("CreateNote");
    }

    [Fact]
    public void The_tag_is_the_feature_folder()
    {
        OpenApiNaming.Tag(typeof(BccCode.Template.Notes.CreateNoteEndpoint)).Should().Be("Notes");
    }
}

public class ContentSecurityPolicyTests
{
    [Fact]
    public void The_shell_policy_allows_only_scripts_and_styles_with_the_nonce_and_no_framing()
    {
        var csp = ContentSecurityPolicy.ForShell("abc");

        csp.Should().Contain("script-src 'self' 'nonce-abc'").And.Contain("style-src 'self' 'nonce-abc'")
            .And.Contain("frame-ancestors 'none'").And.Contain("object-src 'none'");
        csp.Should().NotContain("unsafe-inline");
    }

    [Fact]
    public void Everything_that_is_no_page_may_load_nothing()
    {
        ContentSecurityPolicy.NO_DOCUMENT.Should().StartWith("default-src 'none'").And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public void Only_the_development_policy_for_the_vite_dev_server_allows_inline_styles()
    {
        ContentSecurityPolicy.VITE_DEV_SERVER.Should().Contain("style-src 'self' 'unsafe-inline'").And.Contain("script-src 'self';");
    }
}

public class SessionClaimsTests
{
    private static ClaimsPrincipal Token(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    [Fact]
    public void Slimming_keeps_what_the_app_reads()
    {
        var principal = Token(new("sub", "subject-1"), new(AuthClaims.PersonUid, "a0000000-0000-4000-8000-000000000001"), new("name", "N"), new("role", "admin"),
            new("email", "x@example.test"), new("birthdate", "2000-01-01"));

        var slim = SessionClaims.Slim(principal);

        slim.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", AuthClaims.PersonUid, "name", "role");
        slim.IsInRole("admin").Should().BeTrue();
        slim.Identity!.Name.Should().Be("N");
    }

    [Fact]
    public void A_token_without_a_person_uid_or_a_subject_is_refused()
    {
        SessionClaims.FromIdToken(Token(new Claim("sub", "s")), "https://idp.example").Should().BeNull();
        SessionClaims.FromIdToken(Token(new Claim(AuthClaims.PersonUid, "a0000000-0000-4000-8000-000000000001")), "https://idp.example").Should().BeNull();
    }

    [Fact]
    public void The_issuer_is_added_and_mfa_is_set_from_amr_only()
    {
        var withMfa = Token(new("sub", "s"), new(AuthClaims.PersonUid, "a0000000-0000-4000-8000-000000000001"), new("amr", "pwd"), new("amr", "mfa"));
        var withoutMfa = Token(new("sub", "s"), new(AuthClaims.PersonUid, "a0000000-0000-4000-8000-000000000001"), new("amr", "pwd"));
        var forged = Token(new("sub", "s"), new(AuthClaims.PersonUid, "a0000000-0000-4000-8000-000000000001"), new(AuthClaims.Mfa, "true"));

        SessionClaims.FromIdToken(withMfa, "https://idp.example")!.FindFirst(AuthClaims.Issuer)!.Value.Should().Be("https://idp.example");
        SessionClaims.FromIdToken(withMfa, "https://idp.example")!.HasClaim(AuthClaims.Mfa, "true").Should().BeTrue();
        SessionClaims.FromIdToken(withoutMfa, "https://idp.example")!.HasClaim(c => c.Type == AuthClaims.Mfa).Should().BeFalse();
        SessionClaims.FromIdToken(forged, "https://idp.example")!.HasClaim(c => c.Type == AuthClaims.Mfa).Should().BeFalse("only the app sets it");
    }
}
