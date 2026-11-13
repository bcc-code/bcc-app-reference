// The local topology. Each resource is set up in the *Resources.cs file named after it; the ports are in RunMode.cs.
var builder = DistributedApplication.CreateBuilder(args);
var run = RunMode.From(builder.Configuration);

var database = builder.AddTemplateDatabase(run);
var schema = builder.AddSchemaStep(database);
var testdata = builder.AddTestData(database, schema);

var api = builder.AddApi(run, database, schema, testdata);
var oidc = builder.AddOidcMock(run, api);
api.WithOidcMock(oidc, run)
    .WithLocalMessaging();

if (run.IsE2E)
{
    // The lean topology: a built frontend served by the API, no Vite, no database UI and no Structurizr.
    api.WithBuiltFrontend();
}
else
{
    api.WithViteFrontend(run);
    builder.AddArchitectureViewer(run);
}

builder.Build().Run();
