using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Api.EfCore.Features.Notes;
using BccCode.Template.Api.EfCore.Infrastructure;
using BccCode.Template.Api.EfCore.Seeding;
using BccCode.Template.Api.EfCore.Tests.Infrastructure;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Api.EfCore.Tests;

[Collection(AppCollectionDefinition.Name)]
public class PersistenceTests(AppFixture app)
{
    [Fact]
    public async Task A_session_survives_the_database_and_back_channel_logout_removes_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessions = app.Host.Services.GetRequiredService<IAuthSessionStorage>();
        var sid = Guid.NewGuid().ToString("N");
        var expires = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await sessions.SaveAsync(new AuthSession("key-" + sid, [1, 2, 3], "https://idp.example", sid, expires), ct);
        await sessions.SaveAsync(new AuthSession("other-" + sid, [4], "https://idp.example", "another-sid", expires), ct);

        (await sessions.FindAsync("key-" + sid, ct))!.ProtectedTicket.Should().Equal(1, 2, 3);
        (await sessions.DeleteByIdentityProviderSessionAsync("https://idp.example", sid, ct)).Should().Be(1);
        (await sessions.FindAsync("key-" + sid, ct)).Should().BeNull();
        (await sessions.FindAsync("other-" + sid, ct)).Should().NotBeNull();
        await sessions.DeleteAsync("other-" + sid, ct);
    }

    [Fact]
    public async Task The_ticket_store_works_end_to_end_on_the_real_storage()
    {
        var store = app.Host.Services.GetRequiredService<SessionTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "s-1"), new Claim("sid", "sid-77"), new Claim("iss", "https://idp.example")], "oidc"));

        var key = await store.StoreAsync(new AuthenticationTicket(principal, new AuthenticationProperties(), "Cookies"));

        (await store.RetrieveAsync(key))!.Principal.FindFirst("sid")!.Value.Should().Be("sid-77");
        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Seeding_adds_the_notes_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = app.Host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>();
        var subject = "seed-" + Guid.NewGuid().ToString("N");
        var time = new FakeTimeProvider();

        await using var db = await factory.CreateDbContextAsync(ct);
        var first = await SeedNotes.RunAsync(db, subject, time, ct);
        await using var again = await factory.CreateDbContextAsync(ct);
        var second = await SeedNotes.RunAsync(again, subject, time, ct);

        first.Should().Be(3);
        second.Should().Be(0);
        await using var read = await factory.CreateDbContextAsync(ct);
        (await read.Notes.CountAsync(n => n.OwnerId == subject && n.Archived, ct)).Should().Be(1);
        (await read.Notes.CountAsync(n => n.OwnerId == subject, ct)).Should().Be(3);
    }

    [Fact]
    public async Task The_key_ring_is_stored_in_the_database_and_a_second_instance_reads_it()
    {
        var keys = new EfDataProtectionKeys(app.Host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>());
        var name = "key-" + Guid.NewGuid().ToString("N");

        keys.StoreElement(new System.Xml.Linq.XElement("key", "one"), name);
        keys.StoreElement(new System.Xml.Linq.XElement("key", "two"), name);

        var stored = keys.GetAllElements().Where(e => e.Value is "one" or "two").ToList();
        stored.Should().ContainSingle().Which.Value.Should().Be("two", "storing the same key again replaces it");
        await using var db = await app.Host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.DataProtectionKeys.Where(k => k.FriendlyName == name).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_note_version_is_a_concurrency_token_that_stops_a_second_writer()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = app.Host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>();
        var (content, _) = NoteContent.Parse("T", "B");
        var note = Note.Create(Guid.CreateVersion7(), "owner-" + Guid.NewGuid().ToString("N"), content, new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        await using (var add = await factory.CreateDbContextAsync(ct))
        {
            add.Notes.Add(note);
            await add.SaveChangesAsync(ct);
        }

        await using var first = await factory.CreateDbContextAsync(ct);
        await using var second = await factory.CreateDbContextAsync(ct);
        (await first.Notes.FindAsync([note.Id], ct))!.Edit(content, note.CreatedAt);
        (await second.Notes.FindAsync([note.Id], ct))!.Edit(content, new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        await first.SaveChangesAsync(ct);

        await second.Invoking(c => c.SaveChangesAsync(ct)).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task The_migrations_match_the_model_and_leave_no_pending_change()
    {
        await using var db = await app.Host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);

        // A model change without `dotnet ef migrations add` would run in tests (the fixture migrates) but fail in a deployed environment.
        db.Database.HasPendingModelChanges().Should().BeFalse("the model changed: run `dotnet ef migrations add <Name>` in api/BccCode.Template.Api.EfCore");
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_served_openapi_document_equals_the_committed_one()
    {
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(CommittedDocument(), TestContext.Current.CancellationToken))!;
        var served = JsonNode.Parse(await GenerateAsync(app.Host.Services))!;

        JsonNode.DeepEquals(served, committed).Should().BeTrue(
            "the contract changed without the committed document: run `dotnet build api/BccCode.Template.Api.EfCore` and commit api/openapi/openapi.json");
    }

    private static async Task<string> GenerateAsync(IServiceProvider services)
    {
        // The service the build-time generator calls (Microsoft.Extensions.ApiDescription.Server). It is internal, so by name.
        var type = Type.GetType("Microsoft.Extensions.ApiDescriptions.IDocumentProvider, Microsoft.AspNetCore.OpenApi")!;
        var provider = services.GetRequiredService(type);
        await using var writer = new StringWriter();
        await (Task)type.GetMethod("GenerateAsync", [typeof(string), typeof(TextWriter)])!.Invoke(provider, ["v1", writer])!;
        return writer.ToString();
    }

    private static string CommittedDocument()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "api", "openapi", "openapi.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("api/openapi/openapi.json was not found above the test output folder.");
    }
}

public class SeedingGateTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Test")]
    [InlineData("Sandbox")]
    [InlineData("Preview")]
    public void Disposable_environments_may_be_seeded(string environment) => SeedingGate.IsAllowed(environment).Should().BeTrue();

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    [InlineData("Staging")]
    [InlineData("Developmnt")]
    [InlineData("")]
    public void Anything_else_is_refused_including_a_misspelled_name(string environment) => SeedingGate.IsAllowed(environment).Should().BeFalse();

    [Fact]
    public void The_seed_plan_has_three_notes_for_the_user_and_one_of_them_is_archived()
    {
        var plan = SeedNotes.Plan("u", new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));

        plan.Should().HaveCount(3).And.OnlyContain(n => n.OwnerId == "u");
        plan.Count(n => n.Archived).Should().Be(1);
        plan.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_seeded_user_is_the_one_the_oidc_mock_signs_in()
    {
        // OidcMock/Personas.cs holds the subject of "user"; the API's development settings must name the same one.
        var settings = JsonNode.Parse(File.ReadAllText(FindUp("api/BccCode.Template.Api.EfCore/appsettings.Development.json")))!;
        var personas = File.ReadAllText(FindUp("api/OidcMock/Personas.cs"));

        personas.Should().Contain($"\"{settings["Seed"]!["UserSubject"]!.GetValue<string>()}\"");
    }

    private static string FindUp(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(relative);
    }
}
