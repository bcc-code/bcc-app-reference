using BccCode.Template.Persistence;
using BccCode.Template.ServiceDefaults;

namespace BccCode.Template.TestData;

/// <summary>
/// The seeder host. It lives in a class instead of Program.cs so that integration tests can build the same host.
/// Local development, E2E and previews only: it is never deployed to sandbox or production; in a preview it runs once from the
/// CI runner (<c>Seeder:Once</c>, ADR 0027).
/// </summary>
public static class TestDataApp
{
    public const string ConnectionName = "template";

    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // The host refuses to start outside the disposable environments, before anything can write.
        SeedingGate.EnsureAllowed(builder.Environment);

        builder.AddServiceDefaults();
        builder.AddNpgsqlDataSource(ConnectionName);
        // The schema step (db-apply) owns the schema. The seeder only writes rows.
        builder.Services.AddTemplateDb(PersistenceSchema.Read(builder.Configuration));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<TestDataLog>();
        builder.Services.AddSingleton<SeedState>();
        builder.Services.AddSingleton<TestDataSeeder>();
        builder.Services.AddHostedService<SeedOnStart>();
        builder.Services.AddHealthChecks().AddCheck<SeedCompletedHealthCheck>("seeded");
        return builder;
    }

    public static void MapEndpoints(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        app.MapPost("/reset-and-seed-data", async (TestDataSeeder seeder, IHostEnvironment environment, CancellationToken cancellationToken) =>
        {
            if (!SeedingGate.IsAllowed(environment.EnvironmentName))
                return Results.Problem($"Resetting data is refused in the environment '{environment.EnvironmentName}'.", statusCode: StatusCodes.Status403Forbidden);
            await seeder.ResetAsync(cancellationToken);
            return Results.Ok(new { notes = SeedNotes.Plan(TimeProvider.System.GetUtcNow()).Count });
        });
    }
}
