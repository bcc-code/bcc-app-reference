using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BccCode.Template.TestData;

/// <summary>
/// Runs every seed step, and the reset. Seeding is idempotent (fixed ids), so it can run on every start and on a database that is
/// already seeded. Both entry points check <see cref="SeedingGate"/>.
/// </summary>
public sealed class TestDataSeeder(IDbContextFactory<TemplateDb> dbs, IHostEnvironment environment, TestDataLog log, SeedState state)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        SeedingGate.EnsureAllowed(environment);
        await using var db = await dbs.CreateDbContextAsync(cancellationToken);
        var added = await SeedNotes.RunAsync(db, SeedNotes.Owner, cancellationToken);
        log.Seeded(added);
        state.Completed = true;
    }

    /// <summary>
    /// Deletes the data of every table, then seeds again. The sign-in sessions stay, so nobody is signed out, and so does the Data
    /// Protection key ring: a running API holds it in memory and does not write the keys again.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        SeedingGate.EnsureAllowed(environment);
        await using (var db = await dbs.CreateDbContextAsync(cancellationToken))
        {
            await db.DeleteAllDataAsync([typeof(AuthSessionRow), typeof(DataProtectionKeyRow)], cancellationToken);
        }

        log.Reset();
        await SeedAsync(cancellationToken);
    }
}

/// <summary>Whether the seed has committed. The AppHost waits for the health check built on it before it starts the API.</summary>
public sealed class SeedState
{
    private volatile bool _completed;

    public bool Completed
    {
        get => _completed;
        set => _completed = value;
    }
}

public sealed class SeedCompletedHealthCheck(SeedState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(state.Completed ? HealthCheckResult.Healthy("seeded") : HealthCheckResult.Unhealthy("The test data is not seeded yet."));
    }
}

/// <summary>
/// Seeds before the server listens: hosted services start in registration order and the web server is the last one, so
/// <c>/health</c> is not served until the seed has committed. A failed seed stops the host, so the API never starts on it.
/// </summary>
public sealed class SeedOnStart(TestDataSeeder seeder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return seeder.SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
