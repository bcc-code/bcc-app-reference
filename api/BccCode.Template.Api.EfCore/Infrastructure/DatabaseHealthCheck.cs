using BccCode.Template.Api.EfCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BccCode.Template.Api.EfCore.Infrastructure;

/// <summary>Part of <c>/health</c> only. It has no "live" tag, so <c>/alive</c> never touches the database.</summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<TemplateDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("select 1", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
