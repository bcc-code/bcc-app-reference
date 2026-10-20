var builder = DistributedApplication.CreateBuilder(args);

var isE2E = builder.Configuration["E2E"] == "true";
var isIsolated = bool.TryParse(builder.Configuration["DcpPublisher:RandomizePorts"], out var randomizePorts) && randomizePorts;
// Fixed ports and a persistent database only for the single default instance. Parallel worktrees (--isolated) and E2E runs
// would otherwise fight over the ports and share one database.
var isSoleInstance = !isE2E && !isIsolated;
int? Port(int port) => isSoleInstance ? port : null;

var postgres = builder.AddPostgres("postgres", port: Port(18010)).WithImageTag("18");
#pragma warning disable ASPIREPROXYENDPOINTS001 // WithEndpointProxySupport is experimental; a persistent container must outlive the proxy
if (isSoleInstance)
    postgres.WithDataVolume("bcc-template-postgres").WithLifetime(ContainerLifetime.Persistent).WithEndpointProxySupport(false);
#pragma warning restore ASPIREPROXYENDPOINTS001
if (!isE2E)
    postgres.WithDbx(dbx => dbx.WithParentRelationship(postgres).WithHostPort(Port(18011)));
// The Pub/Sub emulator: the outbox publishes to it and the setup step below points a push subscription at the API, so a local
// run uses the path that runs in GCP. The pinned image is google-cloud-cli 587.0.0-emulators; pin a newer digest on purpose.
const string pubsubProject = "template-local";
var pubsub = builder.AddContainer("pubsub", "gcr.io/google.com/cloudsdktool/google-cloud-cli")
    .WithImageSHA256("8d8573471c489a409f03891d2c24d177fa531276184f7002b9a2d853d1b3ed54")
    .WithHttpEndpoint(targetPort: 8085, port: Port(18020), name: "pubsub")
    .WithEntrypoint("gcloud")
    .WithArgs("beta", "emulators", "pubsub", "start", "--host-port=0.0.0.0:8085", $"--project={pubsubProject}")
    // "Running" only means the container started; the emulator takes a while to listen.
    .WithHttpHealthCheck("/", endpointName: "pubsub")
    // The emulator calls the API on the host; Docker Desktop has this name, Linux needs it.
    .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway");

// template:remove-on-init:begin
// Template:Persistence picks the variant (marten or efcore, default marten). Each variant gets its own database name, so
// switching variant on the persistent local volume never puts two schemas in one database. The connection name stays
// "template", which is what both APIs read.
var persistence = builder.Configuration["Template:Persistence"] ?? "marten";
var database = postgres.AddDatabase("template", persistence == "efcore" ? "template_efcore" : "template");
// template:remove-on-init:end
// template:init-marten var database = postgres.AddDatabase("template");
// template:init-efcore var database = postgres.AddDatabase("template");

// The three steps of one persistence variant, in the order they must finish: the schema, the seed data, then the API.
// Schema and seed run the API's own host with a command, so they use the same registrations as the API itself.
IResourceBuilder<ProjectResource> AddVariant<TProject>(string[] schemaCommand, string[] seedCommand) where TProject : IProjectMetadata, new()
{
    IResourceBuilder<ProjectResource> Add(string name, string[]? command = null)
    {
        var project = builder.AddProject<TProject>(name, launchProfileName: null)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
            .WithReference(database)
            .WaitFor(database);
        return command is null ? project : project.WithArgs(command);
    }

    var schema = Add("schema", schemaCommand);
    // Never at API startup (standard 12): locally the API waits for the schema step, in GCP a Cloud Run job runs it.
    var seed = Add("seed", seedCommand).WaitForCompletion(schema);
    return Add("api").WithHttpsEndpoint(port: Port(18000), name: "https")
        // Health checks only: CI runners do not trust the ASP.NET dev certificate. Browsers use https.
        .WithHttpEndpoint(name: "http")
        .WaitForCompletion(seed);
}

// template:remove-on-init:begin
// The schema step of Marten is JasperFx "resources setup"; the one of EF Core is the API's own "migrate" command (EF Core
// migrations plus Wolverine's tables). Both run before the seed step and the API.
var api = persistence switch
{
    "marten" => AddVariant<Projects.BccCode_Template_Api_Marten>(["resources", "setup"], ["seed"]),
    "efcore" => AddVariant<Projects.BccCode_Template_Api_EfCore>(["migrate"], ["seed"]),
    _ => throw new InvalidOperationException($"Template:Persistence must be 'marten' or 'efcore', not '{persistence}'."),
};
// template:remove-on-init:end
// template:init-marten var api = AddVariant<Projects.BccCode_Template_Api_Marten>(["resources", "setup"], ["seed"]);
// template:init-efcore var api = AddVariant<Projects.BccCode_Template_Api_EfCore>(["migrate"], ["seed"]);

// The emulator pushes from its own container, so the target is the API's http endpoint as Aspire exposes it to containers. A
// hand-built host.docker.internal URL with the host port fails on Linux: that port is a proxy on 127.0.0.1 only. The API's
// https endpoint is not used: the container does not trust the ASP.NET development certificate.
var apiFromContainers = api.GetEndpoint("http", KnownNetworkIdentifiers.DefaultAspireContainerNetwork).Property(EndpointProperty.Url);
var pubsubSetup = builder.AddContainer("pubsub-setup", "curlimages/curl")
    .WithImageSHA256("c1fe1679c34d9784c1b0d1e5f62ac0a79fca01fb6377cdd33e90473c6f9f9a69")   // 8.11.1
    .WithBindMount("../../tools/pubsub-setup", "/setup", isReadOnly: true)
    .WithEntrypoint("sh")
    .WithArgs("/setup/setup.sh")
    .WithEnvironment("PUBSUB", pubsub.GetEndpoint("pubsub"))
    .WithEnvironment("PROJECT", pubsubProject)
    .WithEnvironment("API", apiFromContainers)
    .WithParentRelationship(pubsub)
    .WaitFor(pubsub);

var oidc = builder.AddProject<Projects.OidcMock>("oidc")
    .WithEnvironment("Client__Urls__0", api.GetEndpoint("https"))
    // The browser uses the *.dev.localhost name (standard 02): it needs no hosts-file entry and gives the app its own cookie scope.
    .WithEnvironment("Client__Urls__1", ReferenceExpression.Create($"https://template.dev.localhost:{api.GetEndpoint("https").Property(EndpointProperty.Port)}"));

var oidcEndpoint = oidc.GetEndpoint(isE2E ? "http" : "https");
api.WithEnvironment("OIDC__Authority", oidcEndpoint)
    // Locally every browser, agent and E2E run shares one client IP, so the production default (30 per minute) trips after a
    // few test runs. Deployed environments keep the default.
    .WithEnvironment("RateLimits__AnonymousPerMinute", "600")
    // Messages go to the emulator. It sends no Google tokens, so the /internal routes are open here; the API refuses this
    // setting outside Development and Test. Deployed environments set the Internal:* audiences and service accounts instead.
    .WithEnvironment("PUBSUB_EMULATOR_HOST", pubsub.GetEndpoint("pubsub").Property(EndpointProperty.HostAndPort))
    .WithEnvironment("Messaging__PubsubProjectId", pubsubProject)
    .WithEnvironment("Messaging__Topic", "template-messages")
    .WithEnvironment("Internal__AllowAll", "true")
    .WaitFor(pubsub)
    .WaitForCompletion(pubsubSetup)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(oidc)
    .WithUrls(context =>
    {
        var endpoint = api.GetEndpoint("https");
        context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://template.dev.localhost:{endpoint.Port}", DisplayText = "Template" });
    });

// There is no Cloud Scheduler locally: sends the outbox rows whose send failed over a minute ago, as the scheduler does every
// minute in GCP.
api.WithHttpCommand("/internal/durability/pass", "Durability pass", "http",
    commandOptions: new HttpCommandOptions { IconName = "BoxCheckmark", Description = "Sends outbox messages whose send failed over a minute ago, as Cloud Scheduler does every minute in GCP" });

if (isE2E)
{
    // E2E: the lean topology. A built frontend served by the API, no Vite and no database UI.
    var staticPath = builder.Configuration["Frontend:StaticPath"]
                     ?? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../frontend/dist"));
    api.WithEnvironment("Frontend__Mode", "Static").WithEnvironment("Frontend__StaticPath", staticPath);
}
else
{
    var frontend = builder.AddViteApp("frontend", "../../frontend").WithPnpm().WithParentRelationship(api);
    api.WithReference(frontend).WithEnvironment("Frontend__Mode", "Vite");
}

builder.Build().Run();
