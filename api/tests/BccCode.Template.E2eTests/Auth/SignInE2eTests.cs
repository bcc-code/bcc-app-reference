using BccCode.Template.E2eTests.Infrastructure;
using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Auth;

public sealed class SignInE2eTests(AspireManager aspire) : E2eTest(aspire)
{
    [Fact]
    public async Task Given_no_session_when_the_app_opens_then_the_browser_ends_on_the_mock_login_page_with_every_persona()
    {
        var page = await NewPageAsync();

        await page.GotoAsync("/");

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

        // The identity provider's page, or the app's own sign-in redirect, shows the personas again.
        await Assertions.Expect(page.Locator("button[name=\"persona\"]").First).ToBeVisibleAsync();
        (await page.APIRequest.GetAsync("/api/me")).Status.Should().Be(401);
    }
}
