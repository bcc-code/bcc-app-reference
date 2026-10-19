using Marten;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BccCode.Template.Api.Marten.Infrastructure;

/// <summary>Part of <c>/health</c> only. It has no "live" tag, so <c>/alive</c> never touches the database.</summary>
public sealed class DatabaseHealthCheck(IDocumentStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var session = store.QuerySession();
        await session.QueryAsync<int>("select 1", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
