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

var oidc = builder.AddProject<Projects.OidcMock>("oidc")
    .WithEnvironment("Client__Urls__0", api.GetEndpoint("https"))
    // The browser uses the *.dev.localhost name (standard 02): it needs no hosts-file entry and gives the app its own cookie scope.
    .WithEnvironment("Client__Urls__1", ReferenceExpression.Create($"https://template.dev.localhost:{api.GetEndpoint("https").Property(EndpointProperty.Port)}"));

var oidcEndpoint = oidc.GetEndpoint(isE2E ? "http" : "https");
api.WithEnvironment("OIDC__Authority", oidcEndpoint)
    // Locally every browser, agent and E2E run shares one client IP, so the production default (30 per minute) trips after a
    // few test runs. Deployed environments keep the default.
    .WithEnvironment("RateLimits__AnonymousPerMinute", "600")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(oidc)
    .WithUrls(context =>
    {
        var endpoint = api.GetEndpoint("https");
        context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://template.dev.localhost:{endpoint.Port}", DisplayText = "Template" });
    });

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
