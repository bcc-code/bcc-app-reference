using BccCode.Template.Admin;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using BccCode.Template.Persistence;
using BccCode.Template.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace BccCode.Template.IntegrationTests;

/// <summary>The notes contract, through the real host and a real Postgres. Each test uses users of its own.</summary>
public class NotesApiTests(AppFixture fixture) : AppTest(fixture)
{

    private async Task<CreateNoteResponse> CreateAsync(TestUser user, string title = "Title", string body = "Body")
    {
        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNoteRequest(title, body)).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
        return (await result.ReadAsJsonAsync<CreateNoteResponse>())!;
    }

    private static async Task<ProblemDetails> ProblemAsync(IScenarioResult result)
    {
        return (await result.ReadAsJsonAsync<ProblemDetails>())!;
    }

    [Fact]
    public async Task Creating_a_note_answers_201_with_a_location_and_the_note()
    {
        var user = TestUser.New();

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNoteRequest("  Groceries ", "Milk")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });

        var note = (await result.ReadAsJsonAsync<CreateNoteResponse>())!;
        note.Title.Should().Be("Groceries");
        note.Version.Should().Be(1);
        note.Id.Version.Should().Be(7);
        result.Context.Response.Headers.Location.ToString().Should().Be($"/api/notes/{note.Id}");
    }

    [Fact]
    public async Task The_list_holds_only_the_callers_notes_newest_first()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<TimeProvider>(time));
        var user = TestUser.New();
        async Task<CreateNoteResponse> Create(TestUser who, string title)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            var result = await host.Scenario(s => { s.As(who); s.Post.Json(new CreateNoteRequest(title, "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
            return (await result.ReadAsJsonAsync<CreateNoteResponse>())!;
        }

        var first = await Create(user, "First");
        var second = await Create(user, "Second");
        await Create(TestUser.New(), "Not mine");

        var result = await host.Scenario(s => { s.As(user); s.Get.Url("/api/notes"); });

        (await result.ReadAsJsonAsync<ListNotesResponse>())!.Notes.Select(n => n.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task A_note_can_be_read_by_its_owner()
    {
        var user = TestUser.New();
        var created = await CreateAsync(user);

        var result = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{created.Id}"); });

        (await result.ReadAsJsonAsync<GetNoteResponse>()).Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task Another_users_note_answers_403_and_a_missing_note_404_and_neither_has_a_body()
    {
        var owner = TestUser.New();
        var stranger = TestUser.New();
        var note = await CreateAsync(owner);

        var foreign = await Host.Scenario(s => { s.As(stranger); s.Get.Url($"/api/notes/{note.Id}"); s.StatusCodeShouldBe(403); });
        var missing = await Host.Scenario(s => { s.As(stranger); s.Get.Url($"/api/notes/{Guid.CreateVersion7()}"); s.StatusCodeShouldBe(404); });

        (await foreign.ReadAsTextAsync()).Should().BeEmpty("a 403 says all there is in its status (standard 09.10)");
        (await missing.ReadAsTextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_edit_changes_the_note_and_moves_the_version_on()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user, "Old", "Old body");

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNoteRequest("New", "New body", note.Version)).ToUrl($"/api/notes/{note.Id}/edit");
            s.StatusCodeShouldBeOk();
        });

        var edited = (await result.ReadAsJsonAsync<EditNoteResponse>())!;
        edited.Should().Match<EditNoteResponse>(n => n.Title == "New" && n.Body == "New body" && n.Version == 2 && n.CreatedAt == note.CreatedAt);
        var read = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<GetNoteResponse>()).Should().BeEquivalentTo(edited);
    }

    [Fact]
    public async Task An_edit_of_a_stale_version_answers_409_and_changes_nothing()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user, "Mine");
        await Host.Scenario(s => { s.As(user); s.Post.Json(new EditNoteRequest("Newer", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit"); });

        var stale = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNoteRequest("Lost update", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit");
            s.StatusCodeShouldBe(409);
        });

        (await ProblemAsync(stale)).Extensions["key"]!.ToString().Should().Be("errors.note.staleVersion");
        var read = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<GetNoteResponse>())!.Title.Should().Be("Newer");
    }

    /// <summary>
    /// Two edits that both pass the pre-check: a writer bumps the stored version after the note was read and before the update
    /// runs. The interceptor stands in for that second writer, so the test reaches the commit-time check the pre-check cannot.
    /// </summary>
    [Fact]
    public async Task An_edit_that_loses_a_race_at_commit_answers_409_and_keeps_the_other_writers_change()
    {
        var racer = new RacingWriter(Fixture.ConnectionString);
        await using var host = await Fixture.StartHostAsync(services: s => s.ConfigureDbContext<TemplateDb>(o => o.AddInterceptors(racer)));
        var user = TestUser.New();
        var note = await host.Scenario(s => { s.As(user); s.Post.Json(new CreateNoteRequest("Mine", "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
        var created = (await note.ReadAsJsonAsync<CreateNoteResponse>())!;
        racer.Arm(created.Id);

        var lost = await host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNoteRequest("Lost update", "", created.Version)).ToUrl($"/api/notes/{created.Id}/edit");
            s.StatusCodeShouldBe(409);
        });

        (await ProblemAsync(lost)).Extensions["key"]!.ToString().Should().Be("errors.conflict", "the commit-time check is the generic conflict");
        var read = await host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{created.Id}"); });
        (await read.ReadAsJsonAsync<GetNoteResponse>())!.Should().Match<GetNoteResponse>(n => n.Title == "Mine" && n.Version == created.Version + 1);
    }

    [Fact]
    public async Task Invalid_input_answers_400_with_the_i18n_key_of_the_first_field_that_fails()
    {
        var user = TestUser.New();

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNoteRequest("   ", new string('x', NoteBody.MaxLength + 1))).ToUrl("/api/notes");
            s.StatusCodeShouldBe(400);
        });

        var problem = await ProblemAsync(result);
        problem.Extensions["key"]!.ToString().Should().Be("errors.note.titleRequired");
        problem.Type.Should().Be("urn:bcc:template:errors.note.titleRequired");
        result.Context.Response.ContentType.Should().StartWith("application/problem+json");
        var body = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNoteRequest("Title", new string('x', NoteBody.MaxLength + 1))).ToUrl("/api/notes");
            s.StatusCodeShouldBe(400);
        });
        (await ProblemAsync(body)).Extensions["key"]!.ToString().Should().Be("errors.note.bodyTooLong");
    }

    [Fact]
    public async Task Archiving_answers_204_and_hides_the_note_from_its_owner()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user);

        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNoteRequest(note.Version)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(204); });

        await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); s.StatusCodeShouldBe(404); });
        var list = await Host.Scenario(s => { s.As(user); s.Get.Url("/api/notes"); });
        (await list.ReadAsJsonAsync<ListNotesResponse>())!.Notes.Should().BeEmpty();
        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNoteRequest(note.Version + 1)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(404); });
    }

    [Fact]
    public async Task Every_write_to_a_foreign_note_answers_403_and_to_an_unknown_one_404_and_stores_nothing()
    {
        var owner = TestUser.New();
        var stranger = TestUser.New();
        var note = await CreateAsync(owner, "Untouched");
        var unknown = Guid.CreateVersion7();
        await using var db = Fixture.NewDb();
        var before = await db.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id, TestContext.Current.CancellationToken);
        var notesBefore = await db.Notes.CountAsync(TestContext.Current.CancellationToken);

        foreach (var (id, status) in new[] { (note.Id, 403), (unknown, 404) })
        {
            // A stale version and an invalid body would be 409 and 400 for the owner: the caller who may not act learns neither.
            await Host.Scenario(s => { s.As(stranger); s.Post.Json(new EditNoteRequest("Hijack", "", 1)).ToUrl($"/api/notes/{id}/edit"); s.StatusCodeShouldBe(status); });
            await Host.Scenario(s => { s.As(stranger); s.Post.Json(new EditNoteRequest(" ", "", 99)).ToUrl($"/api/notes/{id}/edit"); s.StatusCodeShouldBe(status); });
            await Host.Scenario(s => { s.As(stranger); s.Post.Json(new ArchiveNoteRequest(1)).ToUrl($"/api/notes/{id}/archive"); s.StatusCodeShouldBe(status); });
        }

        var after = await db.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id, TestContext.Current.CancellationToken);
        (after.Version, after.Title, after.UpdatedAt).Should().Be((before.Version, before.Title, before.UpdatedAt));
        (await db.Notes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(notesBefore);
        (await db.Notes.AnyAsync(n => n.Id == unknown, TestContext.Current.CancellationToken)).Should().BeFalse();
        var read = await Host.Scenario(s => { s.As(owner); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<GetNoteResponse>())!.Title.Should().Be("Untouched");
    }

    [Fact]
    public async Task Me_returns_the_display_name_and_roles_and_nothing_else()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/me"); });

        var json = await result.ReadAsTextAsync();
        (await result.ReadAsJsonAsync<GetMeResponse>()).Should().BeEquivalentTo(new GetMeResponse("Test Admin", ["admin"]));
        json.Should().NotContain("sub").And.NotContain("-");
    }

    [Fact]
    public async Task Admin_stats_count_active_and_archived_notes()
    {
        var user = TestUser.New();
        async Task<GetStatsResponse> StatsAsync()
        {
            return (await (await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); })).ReadAsJsonAsync<GetStatsResponse>())!;
        }

        var before = await StatsAsync();
        await CreateAsync(user);
        var archived = await CreateAsync(user);
        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNoteRequest(archived.Version)).ToUrl($"/api/notes/{archived.Id}/archive"); s.StatusCodeShouldBe(204); });

        var after = await StatsAsync();

        after.ActiveNotes.Should().Be(before.ActiveNotes + 1);
        after.ArchivedNotes.Should().Be(before.ArchivedNotes + 1);
    }
}

/// <summary>
/// Replaces the version of one note through its own connection right before EF's update of that note runs: another writer's
/// change.
/// </summary>
internal sealed class RacingWriter(string connectionString) : SaveChangesInterceptor
{
    private Guid? _armedFor;

    public void Arm(Guid noteId)
    {
        _armedFor = noteId;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_armedFor is { } id && eventData.Context!.ChangeTracker.Entries<Note>().Any(e => e.State == EntityState.Modified && e.Entity.Id == id))
        {
            _armedFor = null;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                update note set version = version + 1 where id = @id;
                """,
                connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }
}
