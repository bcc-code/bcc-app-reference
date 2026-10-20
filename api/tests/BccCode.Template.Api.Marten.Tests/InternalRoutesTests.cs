using System.Diagnostics;
using BccCode.Template.Api.Marten.Tests.Infrastructure;
using BccCode.Template.Web.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BccCode.Template.Api.Marten.Tests;

/// <summary>The routes under <c>/internal</c>: the policy each needs, who is refused, and the schema step that creates the envelope tables.</summary>
[Collection(AppCollectionDefinition.Name)]
public class InternalRoutesTests(AppFixture app)
{
    private const string Schema = "public";

    private static readonly Dictionary<string, string?> Configured = new()
    {
        ["Internal:AllowAll"] = "false",
        ["Internal:PubSubAudience"] = "https://template.test/internal/pubsub",
        ["Internal:PubSubServiceAccount"] = "pubsub-push@test.iam.gserviceaccount.com",
        ["Internal:SchedulerAudience"] = "https://template.test/internal/scheduler",
        ["Internal:SchedulerServiceAccount"] = "scheduler@test.iam.gserviceaccount.com",
    };

    [Fact]
    public void Every_internal_route_has_its_internal_policy_and_none_allows_anonymous_or_gets_the_user_policy()
    {
        var internalRoutes = app.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/internal/", StringComparison.Ordinal)).ToList();

        internalRoutes.Select(e => e.RoutePattern.RawText).Should().BeEquivalentTo("/internal/pubsub/{subscription}", "/internal/durability/pass");
        foreach (var endpoint in internalRoutes)
        {
            var route = endpoint.RoutePattern.RawText!;
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull($"{route} must not allow anonymous access");
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should().Equal(InternalEndpoints.ExpectedPolicy(route));
        }
        InternalRouteGuard.FindViolations(internalRoutes).Should().BeEmpty();
    }

    [Fact]
    public async Task The_internal_routes_are_not_in_the_openapi_document()
    {
        await using var host = await app.StartHostAsync();
        var type = Type.GetType("Microsoft.Extensions.ApiDescriptions.IDocumentProvider, Microsoft.AspNetCore.OpenApi")!;
        await using var writer = new StringWriter();
        await (Task)type.GetMethod("GenerateAsync", [typeof(string), typeof(TextWriter)])!.Invoke(host.Services.GetRequiredService(type), ["v1", writer])!;

        writer.ToString().Should().NotContain("/internal");
    }

    [Theory]
    [InlineData("/internal/durability/pass")]
    [InlineData("/internal/pubsub/messages")]
    public async Task Without_AllowAll_a_call_with_no_token_and_a_signed_in_user_are_both_401(string path)
    {
        await using var host = await app.StartHostAsync(Configured);

        await host.Scenario(s => { s.Post.Text("{}").ToUrl(path); s.StatusCodeShouldBe(401); });
        await host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Post.Text("{}").ToUrl(path); s.StatusCodeShouldBe(401); });
    }

    [Fact]
    public async Task A_host_without_the_internal_settings_and_without_AllowAll_does_not_start()
    {
        var act = () => app.StartHostAsync(new Dictionary<string, string?> { ["Internal:AllowAll"] = "false", ["Internal:PubSubAudience"] = "" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Internal:PubSubAudience*");
    }

    [Fact]
    public async Task A_host_without_a_pubsub_project_does_not_start()
    {
        var act = () => app.StartHostAsync(new Dictionary<string, string?> { ["Messaging:PubsubProjectId"] = "" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Messaging:PubsubProjectId*");
    }

    /// <summary>
    /// The schema step run as a real environment runs it, on an empty database and with no Pub/Sub or Internal settings (the Cloud
    /// Run migrate job has none): it must create the envelope tables, including the outgoing timestamp column and its index that the
    /// durability pass needs. The API itself creates nothing outside Development and Test.
    /// </summary>
    [Fact]
    public async Task The_schema_step_creates_the_envelope_tables_with_the_timestamp_column_and_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = "schema_step_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(app.ConnectionString, $"create database {database}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(app.ConnectionString) { Database = database }.ConnectionString;

        var assembly = BuiltApi();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(assembly)!, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { assembly, "resources", "setup" }) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
        start.Environment["ConnectionStrings__template"] = connectionString;
        start.Environment["Frontend__Mode"] = "Static";
        start.Environment["Frontend__StaticPath"] = TestFrontend.Path;
        start.Environment.Remove("PUBSUB_EMULATOR_HOST");   // the step must not touch Pub/Sub
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMinutes(2), ct);

        process.ExitCode.Should().Be(0, await output + await errors);
        var columns = await QueryAsync(connectionString,
            $"select column_name from information_schema.columns where table_schema = '{Schema}' and table_name = 'wolverine_outgoing_envelopes'", ct);
        columns.Should().Contain("timestamp");
        var indexes = await QueryAsync(connectionString,
            $"select indexname from pg_indexes where schemaname = '{Schema}' and tablename = 'wolverine_outgoing_envelopes'", ct);
        indexes.Should().Contain("idx_wolverine_outgoing_envelopes_stale");
        var tables = await QueryAsync(connectionString, $"select table_name from information_schema.tables where table_schema = '{Schema}'", ct);
        tables.Should().Contain(["wolverine_incoming_envelopes", "wolverine_dead_letters"]);
    }

    /// <summary>
    /// The API's own build output, not the test folder: it holds the test framework's assemblies, which JasperFx's command scan
    /// cannot load. The folder is next to the test project's, in the same configuration.
    /// </summary>
    private static string BuiltApi()
    {
        var name = typeof(Program).Assembly.GetName().Name!;
        var framework = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        for (var directory = framework; directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "api", name, "bin", framework.Parent!.Name, framework.Name, name + ".dll");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"The build output of {name} was not found above the test output folder.");
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var values = new List<string>();
        while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0));
        return values;
    }
}
