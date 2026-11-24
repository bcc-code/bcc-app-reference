using BccCode.Template.Notes;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Tests.Notes;

/// <summary>The slices' pure steps, called directly with values: no host, no database, no mocks (standard 09, rule 2).</summary>
public class NoteSliceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Stranger = Guid.Parse("a0000000-0000-4000-8000-000000000002");

    private static CallerAccess Caller(Guid personUid)
    {
        return new CallerAccess(personUid, new HashSet<string>());
    }

    private static FakeTimeProvider Clock(DateTimeOffset? at = null)
    {
        return new FakeTimeProvider(at ?? Now);
    }

    private static NoteContent Content(string title = "T", string body = "B")
    {
        return NoteContent.Parse(title, body);
    }

    /// <summary>A note at the given version: created at version 1, then edited until it has moved on.</summary>
    private static Note Stored(Guid? owner = null, int version = 3)
    {
        var note = Note.Create(Guid.CreateVersion7(), Now.AddDays(-1), owner ?? Owner, Content());
        while (note.Version < version) note.Edit(Now.AddHours(-1), Content());
        return note;
    }

    private static int? StatusOf(IResult? result)
    {
        return (result as IStatusCodeHttpResult)?.StatusCode;
    }

    /// <summary>What Permission answers: the status of the one result inside the <c>Results</c>, or null when the request goes on.</summary>
    private static int? PermissionStatus(Results<NotFound, Forbidden>? permission)
    {
        return StatusOf(permission?.Result);
    }

    [Fact]
    public void Creating_adds_a_note_at_version_1_owned_by_the_caller()
    {
        var request = new CreateNoteRequest(" Title ", "Body");
        CreateNoteEndpoint.Validate(request).Should().BeNull();

        var (response, insert) = CreateNoteEndpoint.Post(request, Caller(Owner), Clock());

        var note = insert.Entity.Should().BeOfType<Note>().Subject;
        (note.Id, note.OwnerId, note.Title, note.Body, note.Version, note.CreatedAt).Should().Be((response.Value!.Id, Owner, "Title", "Body", 1, Now));
        note.Id.Version.Should().Be(7);
        response.Location.Should().Be($"/api/notes/{note.Id}");
        response.Value.Version.Should().Be(1);
    }

    [Fact]
    public void An_invalid_title_or_body_is_a_400_with_the_key_of_the_first_field_that_fails()
    {
        CreateNoteEndpoint.Validate(new CreateNoteRequest(" ", "")).Should().BeEquivalentTo(new DomainError("errors.note.titleRequired", "Invalid title"));
        CreateNoteEndpoint.Validate(new CreateNoteRequest(new string('x', 201), "")).Should().BeEquivalentTo(new DomainError("errors.note.titleTooLong", "Invalid title"));
        CreateNoteEndpoint.Validate(new CreateNoteRequest("T", new string('x', 10_001))).Should().BeEquivalentTo(new DomainError("errors.note.bodyTooLong", "Invalid body"));
        CreateNoteEndpoint.Validate(new CreateNoteRequest(" ", new string('x', 10_001)))!.Key.Should().Be("errors.note.titleRequired");
    }

    [Fact]
    public void Permission_answers_404_when_there_is_no_note_and_403_when_it_is_someone_elses()
    {
        var note = Stored();

        PermissionStatus(EditNoteEndpoint.Permission(null, Caller(Owner))).Should().Be(404, "unknown, or archived: the query filter hides it");
        PermissionStatus(EditNoteEndpoint.Permission(note, Caller(Stranger))).Should().Be(403);
        EditNoteEndpoint.Permission(note, Caller(Owner)).Should().BeNull();
        PermissionStatus(ArchiveNoteEndpoint.Permission(note, Caller(Stranger))).Should().Be(403);
        PermissionStatus(GetNoteEndpoint.Permission(note, Caller(Stranger))).Should().Be(403);
        PermissionStatus(GetNoteEndpoint.Permission(null, Caller(Owner))).Should().Be(404);
    }

    [Fact]
    public void An_edit_on_a_stale_version_is_a_409_and_the_body_is_checked_after_that()
    {
        var note = Stored(version: 3);

        EditNoteEndpoint.Before(new EditNoteRequest("New", "", 1), note).Should().BeOfType<StaleVersion>().Which.CurrentVersion.Should().Be(3);
        EditNoteEndpoint.Before(new EditNoteRequest("New", "", 3), note).Should().BeNull();
        EditNoteEndpoint.Validate(new EditNoteRequest(" ", "", 3)).Should().NotBeNull();
        EditNoteEndpoint.Validate(new EditNoteRequest("New", "", 3)).Should().BeNull();
    }

    [Fact]
    public void An_edit_changes_the_note_and_moves_the_version_on()
    {
        var note = Stored(version: 3);
        var request = new EditNoteRequest("New", "Body", 3);

        var response = EditNoteEndpoint.Post(request, note, Clock());

        response.Should().Be(new EditNoteResponse(note.Id, "New", "Body", note.CreatedAt, Now, 4));
        note.Version.Should().Be(4);
    }

    [Fact]
    public void Archiving_answers_409_on_a_stale_version()
    {
        var note = Stored(version: 2);

        ArchiveNoteEndpoint.Before(new ArchiveNoteRequest(1), note).Should().BeOfType<StaleVersion>();
        ArchiveNoteEndpoint.Before(new ArchiveNoteRequest(2), note).Should().BeNull();
    }

    [Fact]
    public void Archiving_removes_the_note_and_moves_the_version_on()
    {
        var note = Stored(version: 2);

        var message = ArchiveNoteEndpoint.Post(new ArchiveNoteRequest(2), note, Clock());

        message.Should().Be(new NoteArchivedMessage(note.Id), "the archive slice hands the message to the outbox as a side effect");
        (note.ArchivedAt, note.Version, note.UpdatedAt).Should().Be((Now, 3, Now));
    }

    [Fact]
    public void The_responses_do_not_expose_the_owner()
    {
        foreach (var type in new[] { typeof(CreateNoteResponse), typeof(EditNoteResponse), typeof(GetNoteResponse), typeof(NoteItem) })
            type.GetProperties().Select(p => p.Name).Should().NotContain("OwnerId", type.Name);
    }

    [Theory]
    [InlineData("", "errors.note.titleRequired")]
    [InlineData("   ", "errors.note.titleRequired")]
    public void A_blank_title_is_refused(string input, string key)
    {
        NoteTitle.TryParse(input, out _).Should().Be(key);
    }

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

    [Fact]
    public void Every_edit_and_archive_moves_the_version_on_by_one()
    {
        var note = Note.Create(Guid.CreateVersion7(), Now, Owner, Content());
        note.Version.Should().Be(1);

        note.Edit(Now.AddMinutes(1), Content("Two"));
        note.Edit(Now.AddMinutes(2), Content("Three"));
        note.Archive(Now.AddMinutes(3));

        (note.Version, note.Title, note.UpdatedAt, note.ArchivedAt).Should().Be((4, "Three", Now.AddMinutes(3), Now.AddMinutes(3)));
    }
}
