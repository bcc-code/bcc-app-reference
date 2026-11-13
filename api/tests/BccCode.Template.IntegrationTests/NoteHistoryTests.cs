using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.IntegrationTests;

/// <summary>
/// ADR 0028 through the endpoints: each step is one change that the row points to, the replaced version is in the history, and a
/// request that fails stores no change. Each test uses users of its own.
/// </summary>
public class NoteHistoryTests(AppFixture fixture) : AppTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<CreateNoteResponse> CreateAsync(TestUser user, string title = "First")
    {
        var result = await Fixture.Host.Scenario(s => { s.As(user); s.Post.Json(new CreateNoteRequest(title, "Body")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
        return (await result.ReadAsJsonAsync<CreateNoteResponse>())!;
    }

    private async Task<EditNoteResponse> EditAsync(TestUser user, CreateNoteResponse note, string title)
    {
        var result = await Fixture.Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNoteRequest(title, "Body", note.Version)).ToUrl($"/api/notes/{note.Id}/edit");
            s.StatusCodeShouldBeOk();
        });
        return (await result.ReadAsJsonAsync<EditNoteResponse>())!;
    }

    [Fact]
    public async Task Each_step_is_a_change_the_row_points_to_and_the_replaced_version_is_in_the_history()
    {
        var user = TestUser.New();

        var created = await CreateAsync(user, "First");
        await using (var db = Fixture.NewDb())
        {
            (await db.NoteVersions.AnyAsync(v => v.Id == created.Id, Ct)).Should().BeFalse("nothing goes to the history until the row is changed");
            var changes = await db.Changes.Where(c => c.SubjectId == created.Id).ToListAsync(Ct);
            var change = changes.Should().ContainSingle().Which;
            change.Type.Should().Be(Note.Created);
            (await db.Notes.SingleAsync(n => n.Id == created.Id, Ct)).ChangeId.Should().Be(change.Id);
            (await db.Actors.SingleAsync(a => a.Id == change.ActorId, Ct)).PersonUid.Should().Be(user.PersonUid, "the change points to the person's alias");
        }

        var edited = await EditAsync(user, created, "Second");
        await Fixture.Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new ArchiveNoteRequest(edited.Version, "Done with it")).ToUrl($"/api/notes/{created.Id}/archive");
            s.StatusCodeShouldBe(204);
        });

        await using var read = Fixture.NewDb();
        var steps = await read.Changes.Where(c => c.SubjectId == created.Id).OrderBy(c => c.Position).ToListAsync(Ct);
        steps.Select(c => c.Type).Should().Equal(Note.Created, Note.Edited, Note.Archived);
        steps.Select(c => c.Reason).Should().Equal(null, null, "Done with it");
        steps.Select(c => c.ActorId).Should().OnlyContain(actor => actor == steps[0].ActorId, "the alias is created on the first change and reused after");
        (await read.Actors.CountAsync(a => a.PersonUid == user.PersonUid, Ct)).Should().Be(1);

        var replaced = await read.NoteVersions.Where(v => v.Id == created.Id).OrderBy(v => v.Version).ToListAsync(Ct);
        replaced.Select(v => (v.Version, v.Title, v.ReplacedBy)).Should().Equal((1, "First", steps[1].Id), (2, "Second", steps[2].Id));
        replaced.Select(v => v.ReplacedAt).Should().Equal(steps[1].At, steps[2].At);

        var current = await read.Notes.IgnoreQueryFilters().SingleAsync(n => n.Id == created.Id, Ct);
        (current.Version, current.ChangeId, current.RemovedAt).Should().Be((3, steps[2].Id, steps[2].At), "an archive keeps the row and says when it was removed");
    }

    [Fact]
    public async Task The_history_lists_the_steps_in_order_with_the_note_as_each_step_left_it()
    {
        var user = TestUser.New();
        var created = await CreateAsync(user, "First");
        await EditAsync(user, created, "Second");

        var result = await Fixture.Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{created.Id}/history"); });

        var history = (await result.ReadAsJsonAsync<GetNoteHistoryResponse>())!;
        history.Id.Should().Be(created.Id);
        history.Entries.Select(e => (e.Step, e.Version, e.Title)).Should().Equal((Note.Created, 1, "First"), (Note.Edited, 2, "Second"));
        history.Entries.Select(e => e.At).Should().BeInAscendingOrder();
        (await result.ReadAsTextAsync()).Should().NotContain(user.PersonUid.ToString(), "no person uid goes to the browser");
    }

    [Fact]
    public async Task The_history_of_a_foreign_note_is_403_and_of_an_unknown_or_archived_one_404()
    {
        var owner = TestUser.New();
        var note = await CreateAsync(owner);

        await Fixture.Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url($"/api/notes/{note.Id}/history"); s.StatusCodeShouldBe(403); });
        await Fixture.Host.Scenario(s => { s.As(owner); s.Get.Url($"/api/notes/{Guid.CreateVersion7()}/history"); s.StatusCodeShouldBe(404); });
        await Fixture.Host.Scenario(s => { s.As(owner); s.Post.Json(new ArchiveNoteRequest(note.Version)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(204); });
        await Fixture.Host.Scenario(s => { s.As(owner); s.Get.Url($"/api/notes/{note.Id}/history"); s.StatusCodeShouldBe(404); });
    }

    [Fact]
    public async Task A_request_that_fails_stores_no_change()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user);
        await using var db = Fixture.NewDb();
        var before = await db.Changes.CountAsync(Ct);

        await Fixture.Host.Scenario(s => { s.As(user); s.Post.Json(new CreateNoteRequest(" ", "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(400); });
        await Fixture.Host.Scenario(s => { s.As(user); s.Post.Json(new EditNoteRequest("", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit"); s.StatusCodeShouldBe(400); });
        await Fixture.Host.Scenario(s => { s.As(user); s.Post.Json(new EditNoteRequest("New", "", note.Version + 1)).ToUrl($"/api/notes/{note.Id}/edit"); s.StatusCodeShouldBe(409); });
        await Fixture.Host.Scenario(s => { s.As(TestUser.New()); s.Post.Json(new EditNoteRequest("New", "", 1)).ToUrl($"/api/notes/{note.Id}/edit"); s.StatusCodeShouldBe(403); });

        (await db.Changes.CountAsync(Ct)).Should().Be(before);
    }
}
