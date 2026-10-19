using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Api.EfCore.Infrastructure;
using JasperFx.CommandLine;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Api.EfCore.Seeding;

public sealed class SeedInput : NetCoreInput;

/// <summary>
/// <c>dotnet run -- seed</c>. A one-shot step the AppHost runs after the migrate step and before the API starts, so the
/// API never starts against an empty database. Exit code 0 is the readiness signal; refusing outside a disposable
/// environment is an error exit, so the API does not start either.
/// </summary>
[Description("Seeds the local test data. Refuses outside a disposable environment.")]
public sealed class SeedCommand : JasperFxAsyncCommand<SeedInput>
{
    public override async Task<bool> Execute(SeedInput input)
    {
        using var host = input.BuildHost();
        var environment = host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        var log = host.Services.GetRequiredService<SeedLog>();
        if (!SeedingGate.IsAllowed(environment))
        {
            log.Refused(environment);
            return false;
        }

        var subject = host.Services.GetRequiredService<IConfiguration>()["Seed:UserSubject"]
                      ?? throw new InvalidOperationException("Seed:UserSubject is not configured.");
        await using var db = await host.Services.GetRequiredService<IDbContextFactory<TemplateDbContext>>().CreateDbContextAsync();
        var added = await SeedNotes.RunAsync(db, subject, host.Services.GetRequiredService<TimeProvider>(), CancellationToken.None);
        log.Seeded(added);
        return true;
    }
}
