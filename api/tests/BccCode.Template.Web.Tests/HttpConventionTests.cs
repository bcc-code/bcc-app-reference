using BccCode.Template.Web.Auth;
using BccCode.Template.Web.Frontend;
using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template.Web.Tests;

public class ProblemsTests
{
    [Fact]
    public void A_problem_has_a_stable_urn_type_and_the_key_as_an_extension()
    {
        var problem = Problems.NotFound();

        problem.Status.Should().Be(404);
        problem.Type.Should().Be("urn:bcc:template:errors.notFound");
        problem.Extensions["key"].Should().Be("errors.notFound");
    }

    [Fact]
    public void A_problem_never_carries_detail() => Problems.Conflict().Detail.Should().BeNull();

    [Fact]
    public void A_validation_problem_maps_each_field_to_i18n_keys()
    {
        var problem = Problems.Validation(new Dictionary<string, string[]> { ["title"] = ["errors.note.titleRequired"] });

        problem.Status.Should().Be(400);
        problem.Errors["title"].Should().Equal("errors.note.titleRequired");
        problem.Extensions["key"].Should().Be("errors.validation");
    }

    [Theory]
    [InlineData(401, "errors.unauthorized")]
    [InlineData(403, "errors.forbidden")]
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
    public void The_policy_follows_from_the_route(string route, string policy) => EndpointConventions.ExpectedPolicy(route).Should().Be(policy);

    [Fact]
    public void The_operation_id_is_the_class_name_without_Endpoint() =>
        OpenApiNaming.OperationId(typeof(Sample.Features.Notes.CreateNoteEndpoint)).Should().Be("CreateNote");

    [Fact]
    public void The_tag_is_the_feature_folder() => OpenApiNaming.Tag(typeof(Sample.Features.Notes.CreateNoteEndpoint)).Should().Be("Notes");
}

public class SpaRouteTests
{
    private static HttpRequest Request(string path, string method = "GET", string accept = "text/html")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        return context.Request;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/notes/0198f5a0-1000-7000-8000-000000000001")]
    public void A_browser_navigation_to_a_client_route_gets_the_shell(string path) => SpaRoute.IsSpaRoute(Request(path)).Should().BeTrue();

    [Theory]
    [InlineData("/api/notes")]
    [InlineData("/user/login")]
    [InlineData("/health")]
    [InlineData("/signin-oidc")]
    [InlineData("/assets/app.js")]
    [InlineData("/favicon.ico")]
    [InlineData("/@vite/client")]
    public void Reserved_routes_and_files_do_not(string path) => SpaRoute.IsSpaRoute(Request(path)).Should().BeFalse();

    [Fact]
    public void A_post_does_not() => SpaRoute.IsSpaRoute(Request("/notes", "POST")).Should().BeFalse();

    [Fact]
    public void A_request_that_does_not_ask_for_html_does_not() => SpaRoute.IsSpaRoute(Request("/notes", accept: "application/json")).Should().BeFalse();
}

public class IndexHtmlTests
{
    [Fact]
    public void Every_script_tag_gets_the_nonce_and_none_is_added_twice()
    {
        var html = """<script type="module" src="/a.js"></script><script nonce="x" src="/b.js"></script><SCRIPT>1</SCRIPT>""";

        IndexHtmlRenderer.AddNonce(html, "abc").Should().Be(
            """<script nonce="abc" type="module" src="/a.js"></script><script nonce="x" src="/b.js"></script><script nonce="abc">1</SCRIPT>""");
    }

    [Fact]
    public void The_csp_allows_only_scripts_with_the_nonce_and_no_framing()
    {
        var csp = ContentSecurityPolicy.For("abc");

        csp.Should().Contain("script-src 'self' 'nonce-abc'").And.Contain("frame-ancestors 'none'").And.Contain("object-src 'none'");
        csp.Should().NotContain("script-src 'self' 'unsafe-inline'");
    }
}

public class SessionClaimsTests
{
    [Fact]
    public void Slimming_keeps_what_the_app_reads_and_adds_a_missing_issuer()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new("sub", "s"), new("name", "N"), new("role", "admin"), new("email", "x@example.test"), new("birthdate", "2000-01-01")], "oidc"));

        var slim = SessionClaims.Slim(principal, "https://idp.example");

        slim.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", "name", "role", "iss");
        slim.IsInRole("admin").Should().BeTrue();
        slim.Identity!.Name.Should().Be("N");
    }
}
