using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// The committed plain-SQL snapshot of the migrated and seeded database (<c>api/BccCode.Template.TestData/Snapshots</c>, written by
/// <c>tools/db-snapshot/update.mjs</c>: many small files, because DCP copies no file over 50 MB into a container). Postgres restores
/// it from its init directory when its data directory is empty; the dashboard command restores it into a running server.
/// </summary>
internal sealed partial record DatabaseSnapshot(string Directory)
{
    private const string DATABASE_NAME = "template";

    // The directory WithInitFiles copies into.
    private const string INIT_DIRECTORY = "/docker-entrypoint-initdb.d";

    public static DatabaseSnapshot For(IDistributedApplicationBuilder builder)
    {
        return new DatabaseSnapshot(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "BccCode.Template.TestData", "Snapshots")));
    }

    /// <summary>
    /// A new data directory starts from the snapshot, so the schema step only applies migrations that are newer and the seeder finds
    /// its rows. Postgres runs init files only on an empty data directory, so a kept volume or a persistent container is left alone.
    /// Outside E2E runs, adds the command that drops the database and restores the snapshot into the running container.
    /// </summary>
    public static void AttachTo(IResourceBuilder<PostgresServerResource> postgres,
        IDistributedApplicationBuilder builder, RunMode run)
    {
        var snapshot = For(builder);
        postgres.WithInitFiles(snapshot.Directory);
        if (!run.IsE2E)
        {
            postgres.WithCommand("reset-from-snapshot", "Reset database from snapshot", snapshot.ResetAsync, new CommandOptions
            {
                Description = "Drops the database, restores the committed snapshot, then applies newer migrations",
                IconName = "DatabaseArrowUp",
                ConfirmationMessage = "Drop the database and restore it from the snapshot?",
            });
        }
    }

    /// <summary>The command: restores the snapshot, then runs the schema step again and waits until the seeder is healthy.</summary>
    private async Task<ExecuteCommandResult> ResetAsync(ExecuteCommandContext context)
    {
        var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
        var containerId = notifications.TryGetCurrentState(context.ResourceName, out var state)
            ? state.Snapshot.Properties.FirstOrDefault(p => p.Name == "container.id")?.Value as string
            : null;
        if (containerId is null)
            return CommandResults.Failure("The Postgres container is not running.");

        var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger("reset-from-snapshot");
        var error = await RestoreIntoContainerAsync(containerId, logger, context.CancellationToken);
        if (error is not null)
            return CommandResults.Failure(error);

        // Applies migrations newer than the snapshot, then the seeder finds its rows (and adds what the snapshot lacks).
        var commands = context.Services.GetRequiredService<ResourceCommandService>();
        foreach (var resource in new[] { "schema", "testdata" })
        {
            Log.Restarting(logger, resource);
            var restart = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, context.CancellationToken);
            if (!restart.Success)
                return CommandResults.Failure($"Restarting {resource} failed: {restart.Message}");

            // The schema step runs to completion; the seeder reports healthy once it has committed.
            if (resource == "schema")
            {
                var finished = await notifications.WaitForResourceAsync(
                    resource, [KnownResourceStates.Finished, KnownResourceStates.Exited, KnownResourceStates.FailedToStart], context.CancellationToken);
                if (finished != KnownResourceStates.Finished)
                    return CommandResults.Failure($"The schema step ended as {finished}.");
            }
            else
            {
                await notifications.WaitForResourceHealthyAsync(resource, context.CancellationToken);
            }
        }

        return CommandResults.Success();
    }

    /// <summary>
    /// Drops the database and restores the snapshot copy that is already in the container. Returns an error message, or null on
    /// success.
    /// </summary>
    private static async Task<string?> RestoreIntoContainerAsync(string containerId, ILogger logger, CancellationToken cancellationToken)
    {
        Log.Dropping(logger, DATABASE_NAME);
        var drop = await RunPsqlAsync(containerId, $"$PSQL -c 'DROP DATABASE IF EXISTS \"{DATABASE_NAME}\" WITH (FORCE)'", logger, cancellationToken);
        if (drop is not null)
            return $"Dropping the database failed: {drop}";

        Log.Restoring(logger);
        var restore = await RunPsqlAsync(
            containerId,
            $"for f in {INIT_DIRECTORY}/*.sql; do $PSQL -f \"$f\" || {{ echo \"$f failed\" >&2; exit 1; }}; done",
            logger, cancellationToken);
        return restore is null ? null : $"Restoring the snapshot failed: {restore}";
    }

    // psql inside the container with the server's own credentials ($PSQL in the command). ON_ERROR_STOP makes a failed statement
    // fail the command instead of leaving a half-restored database. The files sort into load order, which is how Postgres's
    // entrypoint runs them too.
    private static async Task<string?> RunPsqlAsync(string containerId, string command, ILogger logger, CancellationToken cancellationToken)
    {
        var script = $"export PGPASSWORD=\"$POSTGRES_PASSWORD\"; PSQL=\"psql -U $POSTGRES_USER -d postgres -v ON_ERROR_STOP=1 -q -o /dev/null\"; {command}";
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "exec", containerId, "sh", "-c", script })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start docker.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var error = (await stderr).Trim();
        foreach (var line in (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Log.PsqlOutput(logger, line);
        }

        if (process.ExitCode == 0)
            return null;

        Log.PsqlFailed(logger, process.ExitCode, error);
        return error.Length > 0 ? error.Split('\n')[^1] : $"exit code {process.ExitCode}";
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Dropping database {Database}")]
        public static partial void Dropping(ILogger logger, string database);

        [LoggerMessage(Level = LogLevel.Information, Message = "Restoring the snapshot")]
        public static partial void Restoring(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Restarting {Resource}")]
        public static partial void Restarting(ILogger logger, string resource);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
        public static partial void PsqlOutput(ILogger logger, string line);

        [LoggerMessage(Level = LogLevel.Error, Message = "psql exited with {ExitCode}: {Error}")]
        public static partial void PsqlFailed(ILogger logger, int exitCode, string error);
    }
}
