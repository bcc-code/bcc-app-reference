using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Seeding;
using BccCode.Template.Api.Marten.Tests.Infrastructure;
using BccCode.Template.Web.Auth;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Api.Marten.Tests;

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
        var store = app.Host.Services.GetRequiredService<IDocumentStore>();
        var subject = "seed-" + Guid.NewGuid().ToString("N");
        var time = new FakeTimeProvider();

        var first = await SeedNotes.RunAsync(store, subject, time, ct);
        var second = await SeedNotes.RunAsync(store, subject, time, ct);

        // The ids are fixed, so the second run finds every stream and adds nothing.
        first.Should().Be(3);
        second.Should().Be(0);
        await using var session = store.QuerySession();
        (await session.Query<Note>().CountAsync(n => n.OwnerId == subject && n.Archived, ct)).Should().Be(1);
    }

    [Fact]
    public async Task The_served_openapi_document_equals_the_committed_one()
    {
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(CommittedDocument(), TestContext.Current.CancellationToken))!;
        var served = JsonNode.Parse(await GenerateAsync(app.Host.Services))!;

        JsonNode.DeepEquals(served, committed).Should().BeTrue(
            "the contract changed without the committed document: run `dotnet build api/BccCode.Template.Api.Marten` and commit api/openapi/openapi.json");
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

        plan.Should().HaveCount(3).And.OnlyContain(n => n.Created.OwnerId == "u");
        plan.Count(n => n.Archived is not null).Should().Be(1);
        plan.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_seeded_user_is_the_one_the_oidc_mock_signs_in()
    {
        // OidcMock/Personas.cs holds the subject of "user"; the API's development settings must name the same one.
        var settings = JsonNode.Parse(File.ReadAllText(FindUp("api/BccCode.Template.Api.Marten/appsettings.Development.json")))!;
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
