using BccCode.Template.Api.Marten.Features.Admin;
using BccCode.Template.Api.Marten.Features.CurrentUser;
using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Tests.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Api.Marten.Tests;

/// <summary>The notes contract, through the real host and a real Postgres. Each test uses users of its own.</summary>
[Collection(AppCollectionDefinition.Name)]
public class NotesApiTests(AppFixture app)
{
    private IAlbaHost Host => app.Host;

    private async Task<NoteResponse> CreateAsync(TestUser user, string title = "Title", string body = "Body")
    {
        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNote(title, body)).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
        return (await result.ReadAsJsonAsync<NoteResponse>())!;
    }

    private static async Task<ProblemDetails> ProblemAsync(IScenarioResult result) => (await result.ReadAsJsonAsync<ProblemDetails>())!;

    [Fact]
    public async Task Creating_a_note_answers_201_with_a_location_and_the_note()
    {
        var user = TestUser.New();

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNote("  Groceries ", "Milk")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });

        var note = (await result.ReadAsJsonAsync<NoteResponse>())!;
        note.Title.Should().Be("Groceries");
        note.Version.Should().Be(1);
        note.Id.Version.Should().Be(7);
        result.Context.Response.Headers.Location.ToString().Should().Be($"/api/notes/{note.Id}");
    }

    [Fact]
    public async Task The_list_holds_only_the_callers_notes_newest_first()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        await using var host = await app.StartHostAsync(services: s => s.AddSingleton<TimeProvider>(time));
        var user = TestUser.New();
        async Task<NoteResponse> Create(TestUser who, string title)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            var result = await host.Scenario(s => { s.As(who); s.Post.Json(new CreateNote(title, "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
            return (await result.ReadAsJsonAsync<NoteResponse>())!;
        }

        var first = await Create(user, "First");
        var second = await Create(user, "Second");
        await Create(TestUser.New(), "Not mine");

        var result = await host.Scenario(s => { s.As(user); s.Get.Url("/api/notes"); });

        (await result.ReadAsJsonAsync<NoteResponse[]>())!.Select(n => n.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task A_note_can_be_read_by_its_owner()
    {
        var user = TestUser.New();
        var created = await CreateAsync(user);

        var result = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{created.Id}"); });

        (await result.ReadAsJsonAsync<NoteResponse>()).Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task Another_users_note_and_a_missing_note_both_answer_404_so_existence_is_not_revealed()
    {
        var owner = TestUser.New();
        var stranger = TestUser.New();
        var note = await CreateAsync(owner);

        var foreign = await Host.Scenario(s => { s.As(stranger); s.Get.Url($"/api/notes/{note.Id}"); s.StatusCodeShouldBe(404); });
        var missing = await Host.Scenario(s => { s.As(stranger); s.Get.Url($"/api/notes/{Guid.CreateVersion7()}"); s.StatusCodeShouldBe(404); });

        (await ProblemAsync(foreign)).Type.Should().Be((await ProblemAsync(missing)).Type).And.Be("urn:bcc:template:errors.notFound");
    }

    [Fact]
    public async Task An_edit_changes_the_note_and_moves_the_version_on()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user, "Old", "Old body");

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNote("New", "New body", note.Version)).ToUrl($"/api/notes/{note.Id}/edit");
            s.StatusCodeShouldBeOk();
        });

        var edited = (await result.ReadAsJsonAsync<NoteResponse>())!;
        edited.Should().Match<NoteResponse>(n => n.Title == "New" && n.Body == "New body" && n.Version == 2 && n.CreatedAt == note.CreatedAt);
        var read = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<NoteResponse>()).Should().BeEquivalentTo(edited);
    }

    [Fact]
    public async Task An_edit_of_a_stale_version_answers_409_and_changes_nothing()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user, "Mine");
        await Host.Scenario(s => { s.As(user); s.Post.Json(new EditNote("Newer", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit"); });

        var stale = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new EditNote("Lost update", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit");
            s.StatusCodeShouldBe(409);
        });

        (await ProblemAsync(stale)).Extensions["key"]!.ToString().Should().Be("errors.note.staleVersion");
        var read = await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<NoteResponse>())!.Title.Should().Be("Newer");
    }

    [Fact]
    public async Task Invalid_input_answers_400_with_an_i18n_key_per_field()
    {
        var user = TestUser.New();

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.Post.Json(new CreateNote("   ", new string('x', NoteBody.MaxLength + 1))).ToUrl("/api/notes");
            s.StatusCodeShouldBe(400);
        });

        var problem = (await result.ReadAsJsonAsync<HttpValidationProblemDetails>())!;
        problem.Errors["title"].Should().Equal("errors.note.titleRequired");
        problem.Errors["body"].Should().Equal("errors.note.bodyTooLong");
    }

    [Fact]
    public async Task Archiving_answers_204_and_hides_the_note_from_its_owner()
    {
        var user = TestUser.New();
        var note = await CreateAsync(user);

        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNote(note.Version)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(204); });

        await Host.Scenario(s => { s.As(user); s.Get.Url($"/api/notes/{note.Id}"); s.StatusCodeShouldBe(404); });
        var list = await Host.Scenario(s => { s.As(user); s.Get.Url("/api/notes"); });
        (await list.ReadAsJsonAsync<NoteResponse[]>()).Should().BeEmpty();
        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNote(note.Version + 1)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(404); });
    }

    [Fact]
    public async Task Every_write_to_an_unknown_or_foreign_note_answers_404_and_stores_nothing()
    {
        var owner = TestUser.New();
        var stranger = TestUser.New();
        var note = await CreateAsync(owner, "Untouched");
        var unknown = Guid.CreateVersion7();
        await using var session = Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        var eventsBefore = await session.Events.FetchStreamStateAsync(note.Id, TestContext.Current.CancellationToken);

        foreach (var id in new[] { note.Id, unknown })
        {
            await Host.Scenario(s => { s.As(stranger); s.Post.Json(new EditNote("Hijack", "", 1)).ToUrl($"/api/notes/{id}/edit"); s.StatusCodeShouldBe(404); });
            await Host.Scenario(s => { s.As(stranger); s.Post.Json(new ArchiveNote(1)).ToUrl($"/api/notes/{id}/archive"); s.StatusCodeShouldBe(404); });
        }

        (await session.Events.FetchStreamStateAsync(note.Id, TestContext.Current.CancellationToken))!.Version.Should().Be(eventsBefore!.Version);
        (await session.Events.FetchStreamStateAsync(unknown, TestContext.Current.CancellationToken)).Should().BeNull();
        var read = await Host.Scenario(s => { s.As(owner); s.Get.Url($"/api/notes/{note.Id}"); });
        (await read.ReadAsJsonAsync<NoteResponse>())!.Title.Should().Be("Untouched");
    }

    [Fact]
    public async Task Me_returns_the_display_name_and_roles_and_nothing_else()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/me"); });

        var json = await result.ReadAsTextAsync();
        (await result.ReadAsJsonAsync<MeResponse>()).Should().BeEquivalentTo(new MeResponse("Test Admin", ["admin"]));
        json.Should().NotContain("sub").And.NotContain("-");
    }

    [Fact]
    public async Task Admin_stats_count_active_and_archived_notes()
    {
        var user = TestUser.New();
        async Task<StatsResponse> StatsAsync() =>
            (await (await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); })).ReadAsJsonAsync<StatsResponse>())!;
        var before = await StatsAsync();
        await CreateAsync(user);
        var archived = await CreateAsync(user);
        await Host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNote(archived.Version)).ToUrl($"/api/notes/{archived.Id}/archive"); s.StatusCodeShouldBe(204); });

        var after = await StatsAsync();

        after.ActiveNotes.Should().Be(before.ActiveNotes + 1);
        after.ArchivedNotes.Should().Be(before.ArchivedNotes + 1);
    }
}
