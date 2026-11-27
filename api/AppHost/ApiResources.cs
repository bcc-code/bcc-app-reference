using Aspire.Hosting.Dotnet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OidcMock;

/// <summary>The API and what it is connected to locally: sign-in, messaging and the frontend.</summary>
internal static class ApiResources
{
    /// <summary>The API, started once the schema is applied and the test data is seeded.</summary>
    public static IResourceBuilder<DotnetProjectResource> AddApi(this IDistributedApplicationBuilder builder, RunMode run,
        IResourceBuilder<PostgresDatabaseResource> database, IResourceBuilder<DotnetProjectResource> schema, IResourceBuilder<DotnetProjectResource> testdata)
    {
        var api = builder.AddTemplateProject("api", "../BccCode.Template.Api/BccCode.Template.Api.csproj")
            .WithHttpsEndpoint(port: run.Port(LocalPorts.Api), name: "https")
            .WithHttpHealthEndpoint()
            .WithReference(database)
            .WaitForCompletion(schema)
            .WaitFor(testdata);
        return api.WithUrls(context =>
        {
            var endpoint = api.GetEndpoint("https");
            context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://{run.BrowserHost}:{endpoint.Port}", DisplayText = "Template" });
        });
    }

    /// <summary>
    /// The address the browser uses: the <c>*.dev.localhost</c> name (standard 02) needs no hosts-file entry and gives the app its
    /// own cookie scope.
    /// </summary>
    public static ReferenceExpression BrowserUrl(this IResourceBuilder<DotnetProjectResource> api, RunMode run)
    {
        var https = api.GetEndpoint("https");
        return ReferenceExpression.Create($"https://{run.BrowserHost}:{https.Property(EndpointProperty.Port)}");
    }

    /// <summary>Signs in against the OIDC mock, which the API waits for.</summary>
    public static IResourceBuilder<DotnetProjectResource> WithOidcMock(this IResourceBuilder<DotnetProjectResource> api,
        IResourceBuilder<DotnetProjectResource> oidc, RunMode run)
    {
        // The API's server-side metadata calls use http under E2E: CI runners do not trust the dev certificate.
        return api.WithEnvironment("Oidc__Authority", oidc.GetEndpoint(run.IsE2E ? "http" : "https"))
            .WithEnvironment("Oidc__ClientId", OidcMockResources.ClientId)
            .WaitFor(oidc);
    }

    /// <summary>
    /// Turns the Entra sign-in on next to Signon, against the mock's <c>template-entra</c> client (ADR 0031). The mock is the Entra
    /// authority (the API accepts that override in Development and Test only), and the two administrator personas are bootstrapped by
    /// their object id, so the first administrator needs no manual grant. Previews do not call this: they stay Signon only.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> WithEntraMock(this IResourceBuilder<DotnetProjectResource> api,
        IResourceBuilder<DotnetProjectResource> oidc, RunMode run)
    {
        api.WithEnvironment("Entra__TenantId", Persona.EntraTenantId)
            .WithEnvironment("Entra__ClientId", Persona.EntraClientId)
            .WithEnvironment("Entra__Authority", oidc.GetEndpoint(run.IsE2E ? "http" : "https"));
        // One setting, as in Secret Manager: <provider>:<id> entries separated by commas.
        api.WithEnvironment("Auth__BootstrapAdmins", string.Join(',', new[] { Persona.Admin, Persona.AdminNoMfa }.Select(p => $"entra:{p.EntraObjectId}")));

        return api;
    }

    /// <summary>
    /// Internal messages go to the durable local inbox (<c>Messaging:UsePubSub</c> is off). In GCP, Cloud Scheduler calls the
    /// durability pass every minute; here a loop calls it every few seconds, so a message cascaded by an endpoint is handled at
    /// once, and a dashboard command runs one pass on demand.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> WithLocalMessaging(this IResourceBuilder<DotnetProjectResource> api)
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
