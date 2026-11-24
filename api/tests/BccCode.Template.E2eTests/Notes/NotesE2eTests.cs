using BccCode.Template.E2eTests.Infrastructure;
using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Notes;

public sealed class NotesE2eTests(AspireManager aspire) : E2eTest(aspire)
{
    private static ILocator Heading(IPage page, string name, int? level = null)
    {
        return page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = name, Level = level });
    }

    private static ILocator EditLink(IPage page, string title)
    {
        return page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = $"Edit {title}" });
    }

    private static ILocator Button(IPage page, string name)
    {
        return page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name });
    }

    private static async Task CreateNoteAsync(IPage page, string title, string? text = null)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New note" }).ClickAsync();
        await page.GetByLabel("Title").FillAsync(title);
        if (text is not null) await page.GetByLabel("Text").FillAsync(text);
        await Button(page, "Create note").ClickAsync();
        await Assertions.Expect(EditLink(page, title)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Given_a_user_when_creating_editing_and_archiving_a_note_then_the_list_follows_and_every_state_is_accessible()
    {
        var title = UniqueTitle("Plan");
        var edited = $"{title} (edited)";
        var page = await NewSignedInPageAsync(Persona.User);
        await Assertions.Expect(Heading(page, "Notes", 1)).ToBeVisibleAsync();
        await Accessibility.AssertNoViolationsAsync(page, "notes list");

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New note" }).ClickAsync();
        await Assertions.Expect(Heading(page, "New note")).ToBeVisibleAsync();
        await Accessibility.AssertNoViolationsAsync(page, "new note");
        await page.GetByLabel("Title").FillAsync(title);
        await page.GetByLabel("Text").FillAsync("First line\nSecond line");
        await Button(page, "Create note").ClickAsync();
        await Assertions.Expect(EditLink(page, title)).ToBeVisibleAsync();
        await Accessibility.AssertNoViolationsAsync(page, "notes list with the new note");

        await EditLink(page, title).ClickAsync();
        await Assertions.Expect(Heading(page, "Edit note")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Title")).ToHaveValueAsync(title);
        await Accessibility.AssertNoViolationsAsync(page, "edit note");
        await page.GetByLabel("Title").FillAsync(edited);
        await Button(page, "Save changes").ClickAsync();
        await Assertions.Expect(EditLink(page, edited)).ToBeVisibleAsync();

        await Button(page, $"Archive {edited}").ClickAsync();
        await Assertions.Expect(EditLink(page, edited)).ToHaveCountAsync(0);
        await Accessibility.AssertNoViolationsAsync(page, "notes list after archiving");
    }

    [Fact]
    public async Task Given_a_note_that_changed_in_the_meantime_when_saving_then_the_conflict_is_shown_and_the_latest_version_offered()
    {
        var title = UniqueTitle("Shared");
        var (context, page) = await NewContextAsync();
        await SignInAsync(page, Persona.User);
        await CreateNoteAsync(page, title);
        await EditLink(page, title).ClickAsync();
        await Assertions.Expect(page.GetByLabel("Title")).ToHaveValueAsync(title);
        var editUrl = page.Url;

        // A second tab saves first, so the first tab holds a stale version.
        var other = await context.NewPageAsync();
        await other.GotoAsync(editUrl);
        await other.GetByLabel("Title").FillAsync($"{title} from the other tab");
        await Button(other, "Save changes").ClickAsync();
        await Assertions.Expect(EditLink(other, $"{title} from the other tab")).ToBeVisibleAsync();
        await other.CloseAsync();

        await page.GetByLabel("Title").FillAsync($"{title} from this tab");
        await Button(page, "Save changes").ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The note was changed after you opened it");
        await Accessibility.AssertNoViolationsAsync(page, "edit note with a conflict");

        await Button(page, "Load the latest version").ClickAsync();
        await Assertions.Expect(page.GetByLabel("Title")).ToHaveValueAsync($"{title} from the other tab");
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);

        await page.GotoAsync("/notes");
        await Button(page, $"Archive {title} from the other tab").ClickAsync();
    }

    [Fact]
    public async Task Given_a_note_of_another_user_when_opening_it_then_it_is_not_listed_and_access_is_refused()
    {
        var title = UniqueTitle("Private");
        var ownerPage = await NewSignedInPageAsync(Persona.User);
        await CreateNoteAsync(ownerPage, title);
        var editUrl = await EditLink(ownerPage, title).GetAttributeAsync("href");

        var otherPage = await NewSignedInPageAsync(Persona.User2);
        await Assertions.Expect(EditLink(otherPage, title)).ToHaveCountAsync(0);
        await otherPage.GotoAsync(editUrl!);
        await Assertions.Expect(otherPage.GetByRole(AriaRole.Alert)).ToContainTextAsync("You do not have access to this.");

        await Button(ownerPage, $"Archive {title}").ClickAsync();
    }
}
