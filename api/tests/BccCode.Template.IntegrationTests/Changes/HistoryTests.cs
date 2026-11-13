using BccCode.Template.Changes;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.IntegrationTests.Changes;

/// <summary>
/// The history mechanism of ADR 0028 on the migrated schema: the trigger refuses a write that skips the change, copies
/// every replaced version, and each audited table has a twin with the same columns.
/// </summary>
public sealed class HistoryTests(AppFixture fixture) : AppTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Change NewChange()
    {
        return new Change(Guid.CreateVersion7(), actorId: null, TimeProvider.System.GetUtcNow());
    }

    private async Task<Guid> CreateAsync(string title = "Note")
    {
        await using var db = Fixture.NewDb();
        var note = Note.Create(Guid.CreateVersion7(), NewChange(), Guid.NewGuid(), NoteContent.Parse(title, "Body"));
        db.Notes.Add(note);
        await db.SaveChangesAsync(Ct);
        return note.Id;
    }

    [Fact]
    public async Task Creating_a_row_writes_version_1_and_nothing_to_the_history()
    {
        var id = await CreateAsync();
        await using var db = Fixture.NewDb();

        (await db.Notes.SingleAsync(n => n.Id == id, Ct)).Version.Should().Be(1);
        (await db.NoteVersions.AnyAsync(v => v.Id == id, Ct)).Should().BeFalse("the history only holds versions that were replaced");
    }

    [Fact]
    public async Task An_update_in_raw_sql_without_a_new_change_is_refused()
    {
        var id = await CreateAsync();
        await using var db = Fixture.NewDb();

#pragma warning disable EF1002 // Table name from the model.
        var update = () => db.Database.ExecuteSqlRawAsync($"update {db.TableOf<Note>()} set title = 'Changed' where id = {{0}}", [id], Ct);
#pragma warning restore EF1002

        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("was updated without a new change");
        (await db.Notes.SingleAsync(n => n.Id == id, Ct)).Title.Should().Be("Note");
        (await db.NoteVersions.AnyAsync(v => v.Id == id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task An_execute_update_that_skips_a_version_is_refused()
    {
        var id = await CreateAsync();
        await using var db = Fixture.NewDb();
        var change = NewChange().Name("NoteRetitled", id);
        db.Changes.Add(change);
        await db.SaveChangesAsync(Ct);

        var update = () => db.Notes.Where(n => n.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ChangeId, change.Id).SetProperty(n => n.Version, 3), Ct);

        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("version 3 does not follow 1");
    }

    [Fact]
    public async Task An_execute_update_with_a_new_change_writes_one_history_row_per_row()
    {
        var first = await CreateAsync("First");
        var second = await CreateAsync("Second");
        await using var db = Fixture.NewDb();
        var change = NewChange().Name("NotesRetitledInBulk", first);
        db.Changes.Add(change);
        await db.SaveChangesAsync(Ct);
        var ids = new[] { first, second };

        var updated = await db.Notes.Where(n => ids.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Title, "Same").SetProperty(n => n.ChangeId, change.Id).SetProperty(n => n.Version, n => n.Version + 1), Ct);

        updated.Should().Be(2);
        var history = await db.NoteVersions.Where(v => ids.Contains(v.Id)).OrderBy(v => v.Title).ToListAsync(Ct);
        history.Select(v => (v.Id, v.Version, v.ReplacedBy)).Should().Equal((first, 1, change.Id), (second, 1, change.Id));
        history.Select(v => v.ReplacedAt).Should().OnlyContain(at => at == change.At, "the version stopped being current when the change that replaced it was made");
    }

    [Fact]
    public async Task A_save_on_a_version_another_request_replaced_fails_on_the_concurrency_token()
    {
        var id = await CreateAsync();
        await using var mine = Fixture.NewDb();
        await using var theirs = Fixture.NewDb();
        var myCopy = await mine.Notes.SingleAsync(n => n.Id == id, Ct);
        var theirCopy = await theirs.Notes.SingleAsync(n => n.Id == id, Ct);
        theirCopy.Edit(NewChange(), NoteContent.Parse("Theirs", ""));
        await theirs.SaveChangesAsync(Ct);

        myCopy.Edit(NewChange(), NoteContent.Parse("Mine", ""));
        var save = () => mine.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task A_delete_copies_the_row_to_the_history_with_no_replacing_change()
    {
        var id = await CreateAsync();
        await using var db = Fixture.NewDb();

        await db.Notes.Where(n => n.Id == id).ExecuteDeleteAsync(Ct);

        var copy = await db.NoteVersions.SingleAsync(v => v.Id == id, Ct);
        (copy.Version, copy.ReplacedBy).Should().Be((1, null));
    }

    [Fact]
    public async Task Every_audited_table_has_a_history_twin_with_the_same_columns_and_the_trigger()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        var columns = await ColumnsAsync(connection);

        var audited = columns.Where(c => c.Column == "change_id" && !c.Table.EndsWith("_history", StringComparison.Ordinal)).Select(c => c.Table).ToList();
        audited.Should().Contain("note");

        foreach (var table in audited)
        {
            var row = columns.Where(c => c.Table == table).Select(c => (c.Column, c.Type)).ToList();
            var history = columns.Where(c => c.Table == table + "_history").ToList();
            history.Should().NotBeEmpty($"{table} has a change_id, so it needs {table}_history (select history_sync('{table}'))");
            history.Select(c => (c.Column, c.Type)).Should().BeEquivalentTo(
                row.Append(("replaced_at", "timestamp with time zone")).Append(("replaced_by", "uuid")),
                $"{table}_history has the columns of {table}; a renamed or retyped column is changed in both");
            history.Where(c => c.NotNull).Select(c => c.Column).Should().BeEquivalentTo(["id", "version", "replaced_at"],
                $"older versions in {table}_history may lack a column that is required now");
            (await HasTriggerAsync(connection, table)).Should().BeTrue($"{table} needs the history_track trigger");
        }

        await using var db = Fixture.NewDb();
        var mapped = db.Model.GetEntityTypes().Where(e => typeof(Audited).IsAssignableFrom(e.ClrType) && !typeof(IReplacedVersion).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName()).ToList();
        audited.Should().BeEquivalentTo(mapped, "every Audited entity is mapped to a table with history, and only those");
    }

    private sealed record ColumnInfo(string Table, string Column, string Type, bool NotNull);

    private static async Task<List<ColumnInfo>> ColumnsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            select c.table_name, c.column_name, c.data_type, c.is_nullable = 'NO'
              from information_schema.columns c
             where c.table_schema = 'public'
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var result = new List<ColumnInfo>();
        while (await reader.ReadAsync(Ct))
            result.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return result;
    }

    private static async Task<bool> HasTriggerAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from pg_trigger where tgrelid = format('public.%I', @table)::regclass and tgname = 'history_track')", connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }
}
