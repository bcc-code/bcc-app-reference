using System.Security.Claims;
using System.Text.Json.Nodes;
using BccCode.Template.Api.Auth;
using BccCode.Template.Api.DataProtection;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BccCode.Template.IntegrationTests;

public class PersistenceTests(AppFixture fixture) : AppTest(fixture)
{
    [Fact]
    public async Task A_session_survives_the_database_and_back_channel_logout_removes_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessions = Fixture.Host.Services.GetRequiredService<IAuthSessionStorage>();
        var sid = Guid.NewGuid().ToString("N");
        var expires = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await sessions.AddAsync(new AuthSession("key-" + sid, [1, 2, 3], "https://idp.example", sid, "s-1", expires), ct);
        await sessions.AddAsync(new AuthSession("other-" + sid, [4], "https://idp.example", "another-sid", "s-1", expires), ct);

        (await sessions.FindAsync("key-" + sid, ct))!.ProtectedTicket.Should().Equal(1, 2, 3);
        (await sessions.DeleteByIdentityProviderSessionAsync("https://idp.example", sid, ct)).Should().Be(1);
        (await sessions.FindAsync("key-" + sid, ct)).Should().BeNull();
        (await sessions.FindAsync("other-" + sid, ct)).Should().NotBeNull();
        await sessions.DeleteAsync("other-" + sid, ct);
    }

    [Fact]
    public async Task The_ticket_store_works_end_to_end_on_the_real_storage()
    {
        var store = Fixture.Host.Services.GetRequiredService<SessionTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "s-1"), new Claim("sid", "sid-77"), new Claim("iss", "https://idp.example")], "oidc"));

        var key = await store.StoreAsync(new AuthenticationTicket(principal, new AuthenticationProperties(), "Cookies"));

        (await store.RetrieveAsync(key))!.Principal.FindFirst("sid")!.Value.Should().Be("sid-77");
        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task The_key_ring_is_stored_in_the_database_and_a_second_instance_reads_it()
    {
        var keys = new EfDataProtectionKeys(Fixture.Host.Services.GetRequiredService<IDbContextFactory<TemplateDb>>());
        var name = "key-" + Guid.NewGuid().ToString("N");

        keys.StoreElement(new System.Xml.Linq.XElement("key", "one"), name);
        keys.StoreElement(new System.Xml.Linq.XElement("key", "two"), name);

        var stored = keys.GetAllElements().Where(e => e.Value is "one" or "two").ToList();
        stored.Should().ContainSingle().Which.Value.Should().Be("two", "storing the same key again replaces it");
        await using var db = await Fixture.Host.Services.GetRequiredService<IDbContextFactory<TemplateDb>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.DataProtectionKeys.Where(k => k.FriendlyName == name).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_served_openapi_document_equals_the_committed_one()
    {
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(CommittedDocument(), TestContext.Current.CancellationToken))!;
        var served = JsonNode.Parse(await GenerateAsync(Fixture.Host.Services))!;

        JsonNode.DeepEquals(served, committed).Should().BeTrue(
            "the contract changed without the committed document: run `pnpm --dir frontend generate` and commit api/BccCode.Template.Api/BccCode.Template.Api.json");
    }

    private static async Task<string> GenerateAsync(IServiceProvider services)
    {
        // The service behind the OpenAPI document of the host (the same document `jasper openapi` writes). It is internal, so by name.
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
            var candidate = Path.Combine(directory.FullName, "api", "BccCode.Template.Api", "BccCode.Template.Api.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("api/BccCode.Template.Api/BccCode.Template.Api.json was not found above the test output folder.");
    }
}

