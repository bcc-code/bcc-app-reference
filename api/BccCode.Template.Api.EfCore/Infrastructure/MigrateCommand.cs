using BccCode.Template.Api.EfCore.Configuration;
using JasperFx.CommandLine;
using Microsoft.EntityFrameworkCore;
using Wolverine.Runtime;

namespace BccCode.Template.Api.EfCore.Infrastructure;

public sealed class MigrateInput : NetCoreInput;

/// <summary>
/// <c>dotnet run -- migrate</c>. Applies the EF Core migrations and the Wolverine message store tables, then exits. It is the only place that changes the schema
/// (standard 12): the AppHost runs it before the seed step and the API, and in GCP a Cloud Run job runs the same command
/// from the API image before a revision rolls out. A failed migration stops the deploy instead of crash-looping the API.
/// </summary>
[Description("Applies the EF Core migrations and the Wolverine tables, then exits.")]
public sealed class MigrateCommand : JasperFxAsyncCommand<MigrateInput>
{
    public override async Task<bool> Execute(MigrateInput input)
    {
        using var host = input.BuildHost();
        await Migrator.RunAsync(host.Services, CancellationToken.None);
        return true;
    }
}

/// <summary>What the migrate step does. The tests call it too, so they run on the schema a real environment gets.</summary>
public static class Migrator
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<TemplateDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);   // takes a database lock, so two instances migrating at once do not collide

        // Wolverine's own tables (the "wolverine" schema): the message store its EF transaction needs.
        await services.GetRequiredService<IWolverineRuntime>().Storage.Admin.MigrateAsync();
    }
}
