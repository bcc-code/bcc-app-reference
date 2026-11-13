using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>The API and what it is connected to locally: sign-in, messaging and the frontend.</summary>
internal static class ApiResources
{
    /// <summary>The API, started once the schema is applied and the test data is seeded.</summary>
    public static IResourceBuilder<ProjectResource> AddApi(this IDistributedApplicationBuilder builder, RunMode run,
        IResourceBuilder<PostgresDatabaseResource> database, IResourceBuilder<ProjectResource> schema, IResourceBuilder<ProjectResource> testdata)
    {
        var api = builder.AddTemplateProject<Projects.BccCode_Template_Api>("api")
            .WithHttpsEndpoint(port: run.Port(LocalPorts.Api), name: "https")
            .WithHttpHealthEndpoint()
            .WithReference(database)
            .WaitForCompletion(schema)
            .WaitFor(testdata);
        return api.WithUrls(context =>
        {
            var endpoint = api.GetEndpoint("https");
            context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://template.dev.localhost:{endpoint.Port}", DisplayText = "Template" });
        });
    }

    /// <summary>
    /// The address the browser uses: the <c>*.dev.localhost</c> name (standard 02) needs no hosts-file entry and gives the app its
    /// own cookie scope.
    /// </summary>
    public static ReferenceExpression BrowserUrl(this IResourceBuilder<ProjectResource> api)
    {
        var https = api.GetEndpoint("https");
        return ReferenceExpression.Create($"https://template.dev.localhost:{https.Property(EndpointProperty.Port)}");
    }

    /// <summary>Signs in against the OIDC mock, which the API waits for.</summary>
    public static IResourceBuilder<ProjectResource> WithOidcMock(this IResourceBuilder<ProjectResource> api,
        IResourceBuilder<ProjectResource> oidc, RunMode run)
    {
        // The API's server-side metadata calls use http under E2E: CI runners do not trust the dev certificate.
        return api.WithEnvironment("Oidc__Authority", oidc.GetEndpoint(run.IsE2E ? "http" : "https"))
            .WithEnvironment("Oidc__ClientId", OidcMockResources.ClientId)
            .WaitFor(oidc);
    }

    /// <summary>
    /// Internal messages go to the durable local inbox (<c>Messaging:UsePubSub</c> is off). In GCP, Cloud Scheduler calls the
    /// durability pass every minute; here a loop calls it every few seconds, so a message cascaded by an endpoint is handled at
    /// once, and a dashboard command runs one pass on demand.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithLocalMessaging(this IResourceBuilder<ProjectResource> api)
    {
        api.ApplicationBuilder.Services.AddHostedService(sp => new DurabilityPassLoop(api.GetEndpoint("http"),
            sp.GetRequiredService<ResourceNotificationService>(), sp.GetRequiredService<ILogger<DurabilityPassLoop>>()));

        // The loop and the command send no Google token, so the /internal routes are open here; the API refuses this setting outside
        // Development and Test. Deployed environments set the Internal:* audiences and service accounts instead.
        return api.WithEnvironment("Internal__AllowAll", "true")
            .WithHttpCommand("/internal/durability/pass", "Durability pass", "http",
                commandOptions: new HttpCommandOptions
                {
                    IconName = "BoxCheckmark",
                    Description = "Runs one durability pass: sends outbox messages and runs the ones in the local inbox, as Cloud Scheduler does every minute in GCP",
                });
    }
}
