using BccCode.Template.Api.Marten.Infrastructure;
using JasperFx.CommandLine;
using Marten;

namespace BccCode.Template.Api.Marten.Seeding;

public sealed class SeedInput : NetCoreInput;

/// <summary>
/// <c>dotnet run -- seed</c>. A one-shot step the AppHost runs after the schema step and before the API starts, so the
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
        var added = await SeedNotes.RunAsync(host.Services.GetRequiredService<IDocumentStore>(), subject, host.Services.GetRequiredService<TimeProvider>(), CancellationToken.None);
        log.Seeded(added);
        return true;
    }
}
