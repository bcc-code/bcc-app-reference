using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);

var isE2E = builder.Configuration["E2E"] == "true";
var isIsolated = bool.TryParse(builder.Configuration["DcpPublisher:RandomizePorts"], out var randomizePorts) && randomizePorts;
// Fixed ports and a persistent database only for the single default instance. Parallel worktrees (--isolated) and E2E runs
// would otherwise fight over the ports and share one database.
var isSoleInstance = !isE2E && !isIsolated;
int? Port(int port)
{
    return isSoleInstance ? port : null;
}

// The port layout of the sole instance, in slots from the base 16000 (scripts/init-app.ps1 -PortBase moves the whole range):
//   +0 API (https), +1 Vite, +10 Postgres, +11 database UI, +20 OIDC mock, +50 dashboard (+51 OTLP, +52 resource service, in
//   Properties/launchSettings.json), +60 Structurizr.
// The slots +30..+32 (platform mock, Mailpit) and +40 (storage emulator) are kept free: an app that needs a stand-in for the BCC
// platform, e-mail or a files bucket uses them, so every app of the family has the same layout.

// Fixed values for the mocks only; none of them is a real secret. The OIDC mock registers its one client with the client id; the
// client is a public PKCE client, so there is no client secret anywhere.
const string OidcClientId = "template";

var postgres = builder.AddPostgres("postgres", port: Port(16010)).WithImageTag("18");
// A persistent container has to outlive the AppHost's endpoint proxy, so the proxy is off for it. WithEndpointProxySupport
// is experimental.
#pragma warning disable ASPIREPROXYENDPOINTS001
if (isSoleInstance)
    postgres.WithDataVolume("bcc-template-postgres").WithLifetime(ContainerLifetime.Persistent).WithEndpointProxySupport(false);
#pragma warning restore ASPIREPROXYENDPOINTS001
// The database UI is a dev-only extra, so E2E runs without it.
if (!isE2E)
    postgres.WithDbx(dbx => dbx.WithParentRelationship(postgres).WithHostPort(Port(16011)));
var database = postgres.AddDatabase("template");

// The same step the deploy runs (`jasper db-apply`: the EF Core migrations, then Wolverine's tables), so the API never creates its
// own schema (standard 12). It runs before the API starts.
var schema = builder.AddProject<Projects.BccCode_Template_Api>("schema", launchProfileName: null)
    .WithArgs("jasper", "db-apply")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(database)
    .WaitFor(database);

// Seeds the test data and exposes the readiness signal. Never deployed outside previews.
var testdata = builder.AddProject<Projects.BccCode_Template_TestData>("testdata", launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint(name: "http")
    .WithReference(database)
    .WaitForCompletion(schema)
    // Without this, WaitFor(testdata) only waits for the process. The seeder reports healthy after the seed has committed.
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithHttpCommand("/reset-and-seed-data", "Reset test data", "http",
        commandOptions: new HttpCommandOptions { IconName = "DatabaseArrowUp", Description = "Deletes the notes and their history and seeds the test data again" });

var api = builder.AddProject<Projects.BccCode_Template_Api>("api", launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpsEndpoint(port: Port(16000), name: "https")
    // Health checks only: CI runners do not trust the ASP.NET dev certificate. Browsers use https.
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithReference(database)
    .WaitForCompletion(schema)
    .WaitFor(testdata);
api.WithUrls(context =>
{
    var endpoint = api.GetEndpoint("https");
    // The browser uses the *.dev.localhost name (standard 02): it needs no hosts-file entry and gives the app its own cookie scope.
    context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://template.dev.localhost:{endpoint.Port}", DisplayText = "Template" });
});

// The local stand-in for BCC Signon, deployed only to previews. Its one client needs the API's base URLs: the browser uses the
// template.dev.localhost name, the tests and the dashboard use localhost.
var apiHttps = api.GetEndpoint("https");
var oidc = builder.AddProject<Projects.OidcMock>("oidc", launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpsEndpoint(port: Port(16020), name: "https")
    // The API's server-side metadata calls use http under E2E: CI runners do not trust the dev certificate.
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("Client__ClientId", OidcClientId)
    .WithEnvironment("Client__Urls__0", apiHttps)
    .WithEnvironment("Client__Urls__1", ReferenceExpression.Create($"https://template.dev.localhost:{apiHttps.Property(EndpointProperty.Port)}"));
oidc.WithUrls(context =>
{
    var endpoint = oidc.GetEndpoint("https");
    context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://localhost:{endpoint.Port}/login", DisplayText = "Persona picker" });
});
var oidcAuthority = oidc.GetEndpoint(isE2E ? "http" : "https");
api.WithEnvironment("Oidc__Authority", oidcAuthority)
    .WithEnvironment("Oidc__ClientId", OidcClientId)
    // Internal messages use the durable local inbox (Messaging:UsePubSub is off), and the durability pass below runs them. The pass
    // and the dashboard command send no Google token, so the /internal routes are open here; the API refuses this setting outside
    // Development and Test. Deployed environments set the Internal:* audiences and service accounts instead.
    .WithEnvironment("Internal__AllowAll", "true")
    .WaitFor(oidc);

// Cloud Scheduler calls the durability pass every minute in GCP; locally this loop stands in for it, every few seconds, so a message
// cascaded by an endpoint is handled at once. The command below runs a pass on demand.
builder.Services.AddHostedService(sp => new DurabilityPassLoop(api.GetEndpoint("http"), sp.GetRequiredService<ResourceNotificationService>(),
    sp.GetRequiredService<ILogger<DurabilityPassLoop>>()));
api.WithHttpCommand("/internal/durability/pass", "Durability pass", "http",
    commandOptions: new HttpCommandOptions { IconName = "BoxCheckmark", Description = "Runs one durability pass: sends outbox messages and runs the ones in the local inbox, as Cloud Scheduler does every minute in GCP" });

if (isE2E)
{
    // E2E: the lean topology. A built frontend served by the API, no Vite, no database UI and no Structurizr.
    var staticPath = builder.Configuration["Frontend:StaticPath"]
                     ?? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../frontend/dist"));
    api.WithEnvironment("Frontend__Mode", "Static").WithEnvironment("Frontend__StaticPath", staticPath);
}
else
{
    var frontend = builder.AddViteApp("frontend", "../../frontend")
        .WithPnpm()
        .WithParentRelationship(api)
        // Vite answers /api with index.html too, so a page opened on its own port breaks. vite.config.ts sends a browser
        // that comes there on to this address, and the dashboard does not list the port.
        .WithEnvironment("TEMPLATE_APP_URL", ReferenceExpression.Create($"https://template.dev.localhost:{apiHttps.Property(EndpointProperty.Port)}"))
        .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);
    if (isSoleInstance) frontend.WithEndpoint("http", endpoint => endpoint.Port = 16001);
    api.WithReference(frontend).WithEnvironment("Frontend__Mode", "Vite");

    // C4 diagrams from docs/architecture/workspace.dsl; refresh the browser after editing the file. The image is pinned in
    // that folder's Dockerfile (stage `structurizr`). Not in E2E runs (standard 02.13).
    var architecture = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../docs/architecture"));
    builder.AddDockerfile("architecture", architecture, stage: "structurizr")
        .WithBindMount(architecture, "/usr/local/structurizr")
        .WithArgs("local")
        .WithHttpEndpoint(port: Port(16060), targetPort: 8080)
        .WithUrlForEndpoint("http", url =>
        {
            url.Url = "/workspace/1/diagrams";
            url.DisplayText = "C4 diagrams";
        });
}

builder.Build().Run();
