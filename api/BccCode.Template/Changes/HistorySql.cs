namespace BccCode.Template.Changes;

/// <summary>
/// The two SQL functions behind the history tables (ADR 0028), created by the first migration in the migration's schema.
/// A migration that creates an <see cref="Audited"/> table, or adds a column to one, ends with
/// <c>migrationBuilder.Sql(HistorySql.Sync("table"))</c>. A rename or type change is written for both tables by hand.
/// </summary>
public static class HistorySql
{
    /// <summary>
    /// Runs before every update and delete of an audited row. Refuses an update that does not point to a new change or
    /// skips a version, then copies the old row to <c>&lt;table&gt;_history</c> with when and by which change it was replaced.
    /// Columns are matched by name, so their order does not matter.
    /// </summary>
    public const string CreateTrackFunction = """
        create function history_track() returns trigger language plpgsql as $$
        declare
          v_at timestamptz := now();
          v_by uuid;
        begin
          if tg_op = 'UPDATE' then
            if new.change_id = old.change_id then
              raise exception '%.% % was updated without a new change', tg_table_schema, tg_table_name, old.id
                using errcode = 'P0001', hint = 'Change the row through an entity method that calls Record(change, ...).';
            end if;
            if new.version <> old.version + 1 then
              raise exception '%.% % version % does not follow %', tg_table_schema, tg_table_name, old.id, new.version, old.version
                using errcode = 'P0001';
            end if;
            execute format('select at from %I.change where id = $1', tg_table_schema) into v_at using new.change_id;
            v_by := new.change_id;
          end if;

          execute format(
            'insert into %1$I.%2$I select (jsonb_populate_record(null::%1$I.%2$I,
               to_jsonb($1) || jsonb_build_object(''replaced_at'', $2, ''replaced_by'', $3))).*',
            tg_table_schema, tg_table_name || '_history') using old, v_at, v_by;

          return coalesce(new, old);
        end $$;
        """;

    /// <summary>
    /// Creates or extends <c>&lt;table&gt;_history</c> in the current schema: every column of the table (nullable, except the
    /// key and <c>replaced_at</c>), plus <c>replaced_at</c> and <c>replaced_by</c>, keyed by (id, version). Attaches the
    /// trigger if it is missing. Safe to run again.
    /// </summary>
    public const string CreateSyncFunction = """
        create function history_sync(p_table text) returns void language plpgsql as $$
        declare
          v_schema text := current_schema();
          v_table regclass := format('%I.%I', current_schema(), p_table)::regclass;
          v_history text := p_table || '_history';
          c record;
        begin
          if to_regclass(format('%I.%I', v_schema, v_history)) is null then
            execute format('create table %I.%I (like %s)', v_schema, v_history, v_table);
            execute format('alter table %I.%I add column replaced_at timestamptz not null, add column replaced_by uuid',
              v_schema, v_history);
            execute format('alter table %I.%I add primary key (id, version)', v_schema, v_history);
          end if;

          for c in
            select a.attname, format_type(a.atttypid, a.atttypmod) as type
              from pg_attribute a
             where a.attrelid = v_table and a.attnum > 0 and not a.attisdropped
               and not exists (select 1 from pg_attribute h
                                where h.attrelid = format('%I.%I', v_schema, v_history)::regclass
                                  and h.attname = a.attname and not h.attisdropped)
          loop
            execute format('alter table %I.%I add column %I %s', v_schema, v_history, c.attname, c.type);
          end loop;

          for c in
            select a.attname
              from pg_attribute a
             where a.attrelid = format('%I.%I', v_schema, v_history)::regclass and a.attnum > 0 and not a.attisdropped
               and a.attnotnull and a.attname not in ('id', 'version', 'replaced_at')
          loop
            execute format('alter table %I.%I alter column %I drop not null', v_schema, v_history, c.attname);
          end loop;

          if not exists (select 1 from pg_trigger where tgrelid = v_table and tgname = 'history_track') then
            execute format('create trigger history_track before update or delete on %s for each row execute function %I.history_track()',
              v_table, v_schema);
          end if;
        end $$;
        """;

    public const string DropFunctions = "drop function history_sync(text); drop function history_track();";

    public static string Sync(string table)
    {
        return $"select history_sync('{table}');";
    }

    public static string DropHistory(string table)
    {
        return $"drop table {table}_history;";
    }
}
