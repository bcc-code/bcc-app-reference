using BccCode.Template.E2eTests.Infrastructure;
using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Auth;

public sealed class SignInE2eTests(AspireManager aspire) : E2eTest(aspire)
{
    [Fact]
    public async Task Given_no_session_when_the_app_opens_then_the_sign_in_page_offers_both_providers_and_signon_leads_to_the_mock_login_page_with_every_persona()
    {
        var page = await NewPageAsync();

        await page.GotoAsync("/");
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/sign-in");
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = ProviderLinkName(SignInProvider.Signon) }).ClickAsync();

        await page.WaitForURLAsync(IsOidc);
        new Uri(page.Url).AbsolutePath.Should().Be("/login");
        foreach (var persona in Persona.All)
            await Assertions.Expect(page.Locator($"button[name=\"persona\"][value=\"{persona.Key}\"]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Given_a_signed_in_user_when_signing_out_then_the_session_is_gone_and_the_personas_are_shown_again()
    {
        var page = await NewSignedInPageAsync(Persona.User);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();

        // The app is back at its root without a session, so it shows the sign-in page again.
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/sign-in");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = ProviderLinkName(SignInProvider.Signon) })).ToBeVisibleAsync();
        (await page.APIRequest.GetAsync("/api/me")).Status.Should().Be(401);
    }
}
