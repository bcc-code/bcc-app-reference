using System.Security.Claims;
using BccCode.Template.Api.EfCore.Features.Notes;
using Microsoft.Extensions.Time.Testing;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Tests;

/// <summary>The slices' pure steps, called directly with values: no host, no database, no mocks (standard 09, rule 2).</summary>
public class NoteSliceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);

    private static ClaimsPrincipal User(string subject) => new(new ClaimsIdentity([new Claim("sub", subject)], "test"));

    private static NoteContent Content(string title = "T", string body = "B") => NoteContent.Parse(title, body).Content;

    /// <summary>A note at the given version: created at version 1, then edited until it has moved on.</summary>
    private static Note Stored(string owner = "owner", int version = 3, bool archived = false)
    {
        var note = Note.Create(Guid.CreateVersion7(), owner, Content(), Now.AddDays(-1));
        while (note.Version < version) note.Edit(Content(), Now.AddHours(-1));
        if (archived) note.Archive(Now.AddHours(-1));
        return note;
    }

    [Fact]
    public void Creating_adds_a_note_at_version_1_owned_by_the_caller()
    {
        var (content, problem) = CreateNoteEndpoint.Validate(new CreateNote(" Title ", "Body"));
        problem.Should().BeSameAs(WolverineContinue.NoProblems);

        var (response, insert) = CreateNoteEndpoint.Post(new CreateNote(" Title ", "Body"), content, User("owner"), _time);

        var note = insert.Entity.Should().BeOfType<Note>().Subject;
        (note.Id, note.OwnerId, note.Title, note.Body, note.Version, note.CreatedAt).Should().Be((response.Value!.Id, "owner", "Title", "Body", 1, Now));
        response.Location.Should().Be($"/api/notes/{note.Id}");
        response.Value.Version.Should().Be(1);
    }

    [Fact]
    public void An_edit_checks_visibility_first_then_freshness_then_the_input()
    {
        var note = Stored(version: 3);
        var invalid = new EditNote(" ", "", Version: 1);

        EditNoteEndpoint.Validate(invalid, note, User("someone-else")).Item2.Status.Should().Be(404, "a foreign note is 404 whatever the body holds");
        EditNoteEndpoint.Validate(invalid, null, User("owner")).Item2.Status.Should().Be(404);
        EditNoteEndpoint.Validate(invalid, Stored(archived: true), User("owner")).Item2.Status.Should().Be(404);
        EditNoteEndpoint.Validate(invalid, note, User("owner")).Item2.Status.Should().Be(409, "then a stale version");
        EditNoteEndpoint.Validate(invalid with { Version = 3 }, note, User("owner")).Item2.Status.Should().Be(400, "then the body");
        EditNoteEndpoint.Validate(new EditNote("New", "", 3), note, User("owner")).Item2.Should().BeSameAs(WolverineContinue.NoProblems);
    }

    [Fact]
    public void An_edit_changes_the_note_and_moves_the_version_on()
    {
        var note = Stored(version: 3);
        var command = new EditNote("New", "Body", 3);
        var (content, _) = EditNoteEndpoint.Validate(command, note, User("owner"));

        var (response, update) = EditNoteEndpoint.Post(note.Id, command, note, content, _time);

        response.Should().Be(new NoteResponse(note.Id, "New", "Body", note.CreatedAt, Now, 4));
        update.Entity.Should().BeSameAs(note);
        note.Version.Should().Be(4);
    }

    [Fact]
    public void Archiving_follows_the_same_404_and_409_rules()
    {
        var note = Stored(version: 2);

        ArchiveNoteEndpoint.Validate(new ArchiveNote(2), note, User("stranger")).Status.Should().Be(404);
        ArchiveNoteEndpoint.Validate(new ArchiveNote(1), note, User("owner")).Status.Should().Be(409);
        ArchiveNoteEndpoint.Validate(new ArchiveNote(2), note, User("owner")).Should().BeSameAs(WolverineContinue.NoProblems);
    }

    [Fact]
    public void Archiving_hides_the_note_and_moves_the_version_on()
    {
        var note = Stored(version: 2);

        var (update, message) = ArchiveNoteEndpoint.Post(note.Id, new ArchiveNote(2), note, _time);

        update.Entity.Should().BeSameAs(note);
        message.Should().Be(new NoteArchivedMessage(note.Id), "the archive slice hands the message to the outbox as a side effect");
        (note.Archived, note.Version, note.UpdatedAt).Should().Be((true, 3, Now));
        NoteRules.IsVisibleTo(note, "owner").Should().BeFalse();
    }

    [Fact]
    public void Reading_hides_what_the_caller_may_not_see()
    {
        GetNoteEndpoint.Validate(null, User("owner")).Status.Should().Be(404);
        GetNoteEndpoint.Validate(Stored(), User("stranger")).Status.Should().Be(404);
        GetNoteEndpoint.Validate(Stored(archived: true), User("owner")).Status.Should().Be(404);
        GetNoteEndpoint.Validate(Stored(), User("owner")).Should().BeSameAs(WolverineContinue.NoProblems);
    }

    [Fact]
    public void The_response_does_not_expose_the_owner()
    {
        typeof(NoteResponse).GetProperties().Select(p => p.Name).Should().NotContain("OwnerId");
        NoteResponse.From(Stored()).Should().BeOfType<NoteResponse>();
    }

    [Theory]
    [InlineData("", "errors.note.titleRequired")]
    [InlineData("   ", "errors.note.titleRequired")]
    public void A_blank_title_is_refused(string input, string key) => NoteTitle.TryParse(input, out _).Should().Be(key);

    [Fact]
    public void A_title_is_trimmed_and_limited_to_200_characters()
    {
        NoteTitle.TryParse("  hi  ", out var title).Should().BeNull();
        title.Value.Should().Be("hi");
        NoteTitle.TryParse(new string('x', 200), out _).Should().BeNull();
        NoteTitle.TryParse(new string('x', 201), out _).Should().Be("errors.note.titleTooLong");
    }

    [Fact]
    public void A_body_may_be_empty_or_null_and_is_limited_to_10000_characters()
    {
        NoteBody.TryParse(null, out var body).Should().BeNull();
        body.Value.Should().BeEmpty();
        NoteBody.TryParse(new string('x', 10_000), out _).Should().BeNull();
        NoteBody.TryParse(new string('x', 10_001), out _).Should().Be("errors.note.bodyTooLong");
    }
}
