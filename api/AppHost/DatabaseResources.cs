using Aspire.Hosting.Dotnet;

/// <summary>Postgres, the schema step that migrates it, and the seeder that fills it.</summary>
internal static class DatabaseResources
{
    /// <summary>
    /// Postgres 18 with the app's database, plus the DBX database UI outside E2E runs. The sole instance keeps its container and
    /// data between runs; an isolated run keeps only its data, in a volume of its own, so a restart of the same worktree skips the
    /// migrations and the seed. A new data directory, and every E2E run, starts from the committed snapshot of the seeded database.
    /// </summary>
    public static IResourceBuilder<PostgresDatabaseResource> AddTemplateDatabase(this IDistributedApplicationBuilder builder, RunMode run)
    {
        // Postgres keeps the password its data directory was created with, so an isolated run, whose volume outlives the
        // AppHost, needs the same password every time. Not a secret: the database holds test data and listens on loopback only.
        var password = run.IsIsolated ? builder.AddParameter("postgres-password", "bcc-template-local-worktree", secret: true) : null;
        var postgres = builder.AddPostgres("postgres", password: password, port: run.Port(LocalPorts.Postgres)).WithImageTag("18");
        // A new data directory starts from the committed snapshot of the seeded database (DatabaseSnapshot.cs).
        DatabaseSnapshot.AttachTo(postgres, builder, run);
        if (run.IsSoleInstance)
            KeepDataBetweenRuns(postgres);
        if (run.DatabaseVolumeName is { } volume)
            postgres.WithDataVolume(volume);
        // The database UI is a dev-only extra, so E2E runs without it.
        if (!run.IsE2E)
            postgres.WithDbx(dbx => dbx.WithParentRelationship(postgres).WithHostPort(run.Port(LocalPorts.DatabaseUi)));
        return postgres.AddDatabase("template");
    }

    /// <summary>
    /// The same step the deploy runs (<c>jasper db-apply</c>: the EF Core migrations, then Wolverine's tables), so the API never
    /// creates its own schema (standard 12). Everything that uses the database waits for it to complete.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> AddSchemaStep(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database)
    {
        return builder.AddTemplateProject("schema", "../BccCode.Template.Api/BccCode.Template.Api.csproj")
            .WithArgs("jasper", "db-apply")
            .WithReference(database)
            .WaitFor(database);
    }

    /// <summary>
    /// Seeds the test data after the schema step. It reports healthy only after the seed has committed, so a resource that waits
    /// for it starts on seeded data. Never deployed outside previews.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> AddTestData(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database, IResourceBuilder<DotnetProjectResource> schema)
    {
        return builder.AddTemplateProject("testdata", "../BccCode.Template.TestData/BccCode.Template.TestData.csproj")
            .WithHttpHealthEndpoint()
            .WithReference(database)
            .WaitForCompletion(schema)
            .WithHttpCommand("/reset-and-seed-data", "Reset test data", "http",
                commandOptions: new HttpCommandOptions
                {
                    IconName = "DatabaseArrowUp",
                    Description = "Deletes the notes and seeds the test data again",
                });
    }

    // A persistent container has to outlive the AppHost's endpoint proxy, so the proxy is off for it.
    private static void KeepDataBetweenRuns(IResourceBuilder<PostgresServerResource> postgres)
    {
        postgres.WithDataVolume("bcc-template-postgres").WithLifetime(ContainerLifetime.Persistent).WithEndpointProxySupport(false);
    }
}
