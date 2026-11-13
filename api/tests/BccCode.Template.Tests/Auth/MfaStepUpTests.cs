using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Http;

namespace BccCode.Template.Tests.Auth;

public sealed class MfaStepUpTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void Only_administrators_who_have_not_stepped_up_yet_are_stepped_up(bool isAdmin, bool alreadySteppedUp, bool expected)
    {
        MfaStepUp.ShouldStepUp(isAdmin, alreadySteppedUp).Should().Be(expected);
    }

    [Fact]
    public void Mfa_in_amr_completes_mfa()
    {
        MfaStepUp.CompletedMfa(["pwd", "mfa"]).Should().BeTrue();
    }

    [Theory]
    [InlineData]
    [InlineData("pwd")]
    [InlineData("MFA")]
    public void Amr_without_mfa_does_not_complete_mfa(params string[] amr)
    {
        MfaStepUp.CompletedMfa(amr).Should().BeFalse();
    }

    private static ClaimsPrincipal Person(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", AuthClaims.Name, AuthClaims.Role));
    }

    private static HttpRequest Request(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context.Request;
    }

    [Fact]
    public void An_administrator_without_mfa_is_refused_on_the_api_only()
    {
        var admin = Person(new Claim(AuthClaims.Role, "admin"));

        AdminMfaMiddleware.RequiresMfa(Request("/api/notes"), admin, "admin").Should().BeTrue();
        AdminMfaMiddleware.RequiresMfa(Request("/auth/logout"), admin, "admin").Should().BeFalse("only /api answers 401 with the step-up problem");
    }

    [Fact]
    public void An_administrator_with_mfa_a_member_and_a_signed_out_request_are_not_refused()
    {
        AdminMfaMiddleware.RequiresMfa(Request("/api/notes"), Person(new Claim(AuthClaims.Role, "admin"), new Claim(AuthClaims.Mfa, "true")), "admin").Should().BeFalse();
        AdminMfaMiddleware.RequiresMfa(Request("/api/notes"), Person(), "admin").Should().BeFalse();
        AdminMfaMiddleware.RequiresMfa(Request("/api/notes"), new ClaimsPrincipal(new ClaimsIdentity()), "admin").Should().BeFalse();
    }

    [Fact]
    public void With_no_admin_role_configured_nobody_is_an_administrator()
    {
        AdminMfaMiddleware.IsAdmin(Person(new Claim(AuthClaims.Role, "admin")), null).Should().BeFalse();
        AdminMfaMiddleware.IsAdmin(Person(new Claim(AuthClaims.Role, "admin")), " ").Should().BeFalse();
        AdminMfaMiddleware.IsAdmin(Person(new Claim(AuthClaims.Role, "admin")), "admin").Should().BeTrue();
    }
}
