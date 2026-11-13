/// <summary>Postgres, the schema step that migrates it, and the seeder that fills it.</summary>
internal static class DatabaseResources
{
    /// <summary>Postgres 18 with the app's database, plus the DBX database UI outside E2E runs.</summary>
    public static IResourceBuilder<PostgresDatabaseResource> AddTemplateDatabase(this IDistributedApplicationBuilder builder, RunMode run)
    {
        var postgres = builder.AddPostgres("postgres", port: run.Port(LocalPorts.Postgres)).WithImageTag("18");
        if (run.IsSoleInstance)
            KeepDataBetweenRuns(postgres);
        // The database UI is a dev-only extra, so E2E runs without it.
        if (!run.IsE2E)
            postgres.WithDbx(dbx => dbx.WithParentRelationship(postgres).WithHostPort(run.Port(LocalPorts.DatabaseUi)));
        return postgres.AddDatabase("template");
    }

    /// <summary>
    /// The same step the deploy runs (<c>jasper db-apply</c>: the EF Core migrations, then Wolverine's tables), so the API never
    /// creates its own schema (standard 12). Everything that uses the database waits for it to complete.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddSchemaStep(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database)
    {
        return builder.AddTemplateProject<Projects.BccCode_Template_Api>("schema")
            .WithArgs("jasper", "db-apply")
            .WithReference(database)
            .WaitFor(database);
    }

    /// <summary>
    /// Seeds the test data after the schema step. It reports healthy only after the seed has committed, so a resource that waits
    /// for it starts on seeded data. Never deployed outside previews.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddTestData(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database, IResourceBuilder<ProjectResource> schema)
    {
        return builder.AddTemplateProject<Projects.BccCode_Template_TestData>("testdata")
            .WithHttpHealthEndpoint()
            .WithReference(database)
            .WaitForCompletion(schema)
            .WithHttpCommand("/reset-and-seed-data", "Reset test data", "http",
                commandOptions: new HttpCommandOptions
                {
                    IconName = "DatabaseArrowUp",
                    Description = "Deletes the notes and their history and seeds the test data again",
                });
    }

    // A persistent container has to outlive the AppHost's endpoint proxy, so the proxy is off for it. WithEndpointProxySupport is
    // experimental.
    private static void KeepDataBetweenRuns(IResourceBuilder<PostgresServerResource> postgres)
    {
#pragma warning disable ASPIREPROXYENDPOINTS001
        postgres.WithDataVolume("bcc-template-postgres").WithLifetime(ContainerLifetime.Persistent).WithEndpointProxySupport(false);
#pragma warning restore ASPIREPROXYENDPOINTS001
    }
}
