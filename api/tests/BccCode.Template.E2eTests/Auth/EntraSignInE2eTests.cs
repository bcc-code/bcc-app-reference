using BccCode.Template.E2eTests.Infrastructure;
using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Auth;

/// <summary>Signing in through the mock's Entra client (ADR 0031): the chooser, a member, the administrators and logout.</summary>
public sealed class EntraSignInE2eTests(AspireManager aspire) : E2eTest(aspire)
{
    private static ILocator Heading(IPage page, string name, int? level = null)
    {
        return page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = name, Level = level });
    }

    private static ILocator Button(IPage page, string name)
    {
        return page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name });
    }

    private static ILocator Button(ILocator scope, string name)
    {
        return scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name });
    }

    [Fact]
    public async Task Given_no_session_when_the_app_opens_then_the_chooser_links_both_providers_and_is_accessible()
    {
        var page = await NewPageAsync();

        await page.GotoAsync("/notes");
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/sign-in");

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = ProviderLinkName(SignInProvider.Signon) })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = ProviderLinkName(SignInProvider.Entra) })).ToBeVisibleAsync();
        await Accessibility.AssertNoViolationsAsync(page, "sign-in page");
    }

    [Fact]
    public async Task Given_an_entra_user_when_creating_a_note_then_it_is_listed_and_the_notes_of_the_signon_person_are_not()
    {
        var title = UniqueTitle("Entra note");
        var page = await NewSignedInPageAsync(Persona.User, provider: SignInProvider.Entra);

        // The same persona is a different person through Entra (no linking): the seeded notes belong to the Signon person.
        await Assertions.Expect(page.GetByText("Welcome", new PageGetByTextOptions { Exact = true })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New note" }).ClickAsync();
        await page.GetByLabel("Title").FillAsync(title);
        await Button(page, "Create note").ClickAsync();

        var edit = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = $"Edit {title}" });
        await Assertions.Expect(edit).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Edit Groceries" })).ToHaveCountAsync(0);
        await Button(page, $"Archive {title}").ClickAsync();
    }

    [Fact]
    public async Task Given_a_bootstrapped_entra_admin_when_opening_the_admin_page_then_a_person_who_signed_in_can_be_made_an_administrator_and_removed_again()
    {
        // Ola signs in first, so she is on the list.
        await NewSignedInPageAsync(Persona.User2, provider: SignInProvider.Entra);
        var page = await NewSignedInPageAsync(Persona.Admin, "/admin/stats", SignInProvider.Entra);

        await Assertions.Expect(Heading(page, "Statistics", 1)).ToBeVisibleAsync();
        await Assertions.Expect(Heading(page, "Administrators")).ToBeVisibleAsync();
        await page.GetByTestId("people-search").FillAsync("Ola");
        await Button(page, "Search").ClickAsync();
        // A Signon sign-in of the same persona is another person in the list, so the row is the Entra one.
        var row = page.GetByRole(AriaRole.Row).Filter(new LocatorFilterOptions { HasText = "Ola Second" }).Filter(new LocatorFilterOptions { HasText = "Microsoft Entra" });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await Accessibility.AssertNoViolationsAsync(page, "admin page with people");

        await Button(row, "Make Ola Second an administrator").ClickAsync();
        await Assertions.Expect(Button(row, "Remove Ola Second as administrator")).ToBeVisibleAsync();
        await Assertions.Expect(row).ToContainTextAsync("Yes");

        await Button(row, "Remove Ola Second as administrator").ClickAsync();
        await Assertions.Expect(Button(row, "Make Ola Second an administrator")).ToBeVisibleAsync();
        await Assertions.Expect(row).ToContainTextAsync("No");
    }

    [Fact]
    public async Task Given_an_entra_admin_without_a_second_factor_when_signing_in_then_the_browser_ends_on_the_second_factor_page_without_a_session()
    {
        var page = await NewPageAsync();

        await PickPersonaAsync(page, Persona.AdminNoMfa, "/admin/stats", SignInProvider.Entra);
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/mfa-required");

        await Assertions.Expect(Heading(page, "Second factor required")).ToBeVisibleAsync();
        (await page.APIRequest.GetAsync("/api/me")).Status.Should().Be(401);
    }

    [Fact]
    public async Task Given_an_entra_session_when_signing_out_then_the_app_is_signed_out()
    {
        var page = await NewSignedInPageAsync(Persona.User, provider: SignInProvider.Entra);

        await Button(page, "Sign out").ClickAsync();

        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == "/auth/sign-in");
        (await page.APIRequest.GetAsync("/api/me")).Status.Should().Be(401);
    }
}
