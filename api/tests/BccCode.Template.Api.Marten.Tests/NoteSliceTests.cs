using System.Security.Claims;
using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Web.Messaging;
using Microsoft.Extensions.Time.Testing;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Tests;

/// <summary>The slices' pure steps, called directly with values: no host, no database, no mocks (standard 09, rule 2).</summary>
public class NoteSliceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);

    private static ClaimsPrincipal User(string subject) => new(new ClaimsIdentity([new Claim("sub", subject)], "test"));

    private static Note Stored(string owner = "owner", int version = 3, bool archived = false) =>
        new() { Id = Guid.CreateVersion7(), OwnerId = owner, Title = "T", Body = "B", Version = version, Archived = archived, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddHours(-1) };

    [Fact]
    public void Creating_starts_a_stream_with_one_event_owned_by_the_caller()
    {
        var (content, problem) = CreateNoteEndpoint.Validate(new CreateNote(" Title ", "Body"));
        problem.Should().BeSameAs(WolverineContinue.NoProblems);

        var (response, start) = CreateNoteEndpoint.Post(new CreateNote(" Title ", "Body"), content, User("owner"), _time);

        start.StreamId.Should().Be(response.Value!.Id);
        start.Events.Should().ContainSingle().Which.Should().Be(new NoteCreated("owner", "Title", "Body", Now));
        response.Location.Should().Be($"/api/notes/{response.Value.Id}");
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
    public void Archiving_appends_an_event_and_hands_the_message_to_the_outbox()
    {
        var note = Stored(version: 2);

        var (append, message) = ArchiveNoteEndpoint.Post(note.Id, new ArchiveNote(2), note, _time);

        append.Should().NotBeNull();
        message.Should().Be(new NoteArchivedMessage(note.Id));
    }

    [Fact]
    public void An_edit_appends_to_the_stream_at_the_version_that_was_read()
    {
        var note = Stored(version: 3);
        var command = new EditNote("New", "Body", 3);
        var (content, _) = EditNoteEndpoint.Validate(command, note, User("owner"));

        var (response, append) = EditNoteEndpoint.Post(note.Id, command, note, content, _time);

        response.Should().Be(new NoteResponse(note.Id, "New", "Body", note.CreatedAt, Now, 4));
        append.Should().NotBeNull();
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

    /// <summary>
    /// Marten skips an event it has no Apply or Create for, without an error: the note would silently ignore a new event.
    /// So every event in the feature must be handled by the aggregate (standard 19, rule 11).
    /// </summary>
    [Fact]
    public void Every_note_event_is_handled_by_the_note_aggregate()
    {
        var events = typeof(Note).Assembly.GetTypes().Where(t => t.Namespace == typeof(Note).Namespace && t.Name.StartsWith("Note", StringComparison.Ordinal) && t.IsSealed
                                                                  && t.GetMethod("<Clone>$") is not null && t != typeof(NoteResponse)
                                                                  && !typeof(IIntegrationMessage).IsAssignableFrom(t));   // messages are not events
        var handled = typeof(Note).GetMethods().Where(m => m.Name is "Apply" or "Create").Select(m => m.GetParameters().Single().ParameterType).ToHashSet();

        events.Should().NotBeEmpty();
        events.Where(e => !handled.Contains(e)).Should().BeEmpty();
    }
}
