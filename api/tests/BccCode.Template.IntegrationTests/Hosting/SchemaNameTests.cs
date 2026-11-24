using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Npgsql;

namespace BccCode.Template.IntegrationTests.Hosting;

/// <summary>A PR preview runs in its own schema: db-apply creates every table there and none in public.</summary>
[Collection(SerialCollectionDefinition.Name)]
public sealed class SchemaNameTests(AppFixture fixture) : AppTest(fixture)
{
    private const string SCHEMA = "pr1";

    [Fact]
    public async Task Db_apply_creates_the_configured_schema_and_the_host_uses_it()
    {
        var connectionString = await NewEmptyDatabaseAsync();
        var settings = new Dictionary<string, string?> { ["Persistence__Schema"] = SCHEMA };

        (await SchemaCommands.RunAsync("db-assert", connectionString, settings)).Should().Be(1, "the migration is not applied yet");
        (await SchemaCommands.RunAsync("db-apply", connectionString, settings)).Should().Be(0);
        (await SchemaCommands.RunAsync("db-assert", connectionString, settings)).Should().Be(0);

        var inSchema = await TablesAsync(connectionString, SCHEMA);
        // Wolverine's envelope tables and the EF Core tables go to the same schema: the migration history
        // and the host's own tables included.
        inSchema.Should().Contain(["wolverine_incoming_envelopes", "wolverine_outgoing_envelopes", "wolverine_dead_letters"]);
        inSchema.Should().Contain(["note", "auth_sessions", "data_protection_keys", "__ef_migrations_history"]);
        (await TablesAsync(connectionString, "public")).Should().BeEmpty("nothing is created in public for a preview");

        // The host maps its tables to the schema, the notes are written to it, and the outbox is there too.
        await using var host = await Fixture.StartHostAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings:template"] = connectionString,
            ["Persistence:Schema"] = SCHEMA,
        });
        var user = TestUser.New();
        var created = await host.Scenario(s => { s.As(user); s.Post.Json(new CreateNoteRequest("In a preview", "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
        var note = (await created.ReadAsJsonAsync<CreateNoteResponse>())!;
        await host.Scenario(s => { s.As(user); s.Post.Json(new EditNoteRequest("Edited", "", note.Version)).ToUrl($"/api/notes/{note.Id}/edit"); s.StatusCodeShouldBeOk(); });
        await host.Scenario(s => { s.As(user); s.Post.Json(new ArchiveNoteRequest(2)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(204); });

        (await CountAsync(connectionString, $"{SCHEMA}.note where version = 3 and archived_at is not null")).Should().Be(1);
        (await TablesAsync(connectionString, "public")).Should().BeEmpty();
    }

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"select count(*) from {table}", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<string>> TablesAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("select table_name from information_schema.tables where table_schema = @schema", connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) tables.Add(reader.GetString(0));
        return tables;
    }

    private async Task<string> NewEmptyDatabaseAsync()
    {
        var name = $"schema_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var create = new NpgsqlCommand($"create database {name}", connection);
        await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = name }.ConnectionString;
    }
}
