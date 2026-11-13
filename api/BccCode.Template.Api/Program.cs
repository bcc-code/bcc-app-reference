using BccCode.Template;
using BccCode.Template.Api.Configuration;
using BccCode.Template.Api.Internal;
using BccCode.Template.ServiceDefaults;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTemplateWeb("Template API");
builder.AddTemplateDb();
builder.AddTemplateWolverine();
builder.Services.AddTemplateLogs();

var app = builder.Build();

app.UseTemplateWeb();
app.MapTemplateApi();
app.MapTemplateInternal(typeof(TemplateAssemblyMarker).Assembly);
// The schema step, codegen and openapi configure no BCC PubSub transport (TemplateMessaging), so there is nothing to map.
if (!CodeGeneration.IsJasperFxCommand()) app.MapTemplateBccPubSub();
app.MapTemplateWeb();
app.MapDefaultEndpoints();
app.AssertTemplateRoutes();

// JasperFx commands (db-apply, db-assert, codegen write, openapi) run only behind the explicit "jasper" prefix; anything
// else serves requests.
if (CodeGeneration.IsJasperFxCommand(args))
{
    // db-apply and db-assert cover the EF Core migrations first (ADR 0028), then Wolverine's tables.
    var migrations = args.Length > 1 ? await TemplateMigrations.RunAsync(app, args[1]) : 0;
    return migrations != 0 ? migrations : await app.RunJasperFxCommands(args[1..]);
}

await app.RunAsync();
return 0;

public partial class Program;
