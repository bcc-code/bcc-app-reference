using BccCode.Template.E2eTests.Infrastructure;
using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Admin;

public sealed class AdminE2eTests(AspireManager aspire) : E2eTest(aspire)
{
    [Fact]
    public async Task Given_an_admin_with_a_second_factor_when_opening_the_statistics_then_the_counts_are_shown()
    {
        var page = await NewSignedInPageAsync(Persona.Admin, "/admin/stats");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Statistics", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("stats-active")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("stats-archived")).ToBeVisibleAsync();
        await Accessibility.AssertNoViolationsAsync(page, "admin statistics");
    }

    [Fact]
    public async Task Given_an_admin_without_a_second_factor_when_signing_in_then_the_browser_ends_on_the_second_factor_page_without_a_session()
    {
        var page = await NewPageAsync();

        await PickPersonaAsync(page, Persona.AdminNoMfa, "/admin/stats");
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/mfa-required");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Second factor required" })).ToBeVisibleAsync();
        (await page.APIRequest.GetAsync("/api/me")).Status.Should().Be(401);
        await Accessibility.AssertNoViolationsAsync(page, "second factor required");
    }

    [Fact]
    public async Task Given_a_plain_user_when_opening_the_statistics_then_the_no_access_page_is_shown_without_a_link_to_them()
    {
        var page = await NewSignedInPageAsync(Persona.User, "/no-access");

        await page.GotoAsync("/admin/stats");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "No access", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Statistics" })).ToHaveCountAsync(0);
        await Accessibility.AssertNoViolationsAsync(page, "no access");
    }
}
