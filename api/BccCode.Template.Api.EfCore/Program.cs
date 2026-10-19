using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Api.EfCore.Infrastructure;
using BccCode.Template.ServiceDefaults;
using BccCode.Template.Web;
using BccCode.Template.Web.Http;
using JasperFx;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTemplateWeb("Template API");
builder.AddTemplateEfCore();
builder.AddTemplateWolverine();
builder.Services.AddTemplateLogs();

var app = builder.Build();

app.UseTemplateWeb();
app.MapTemplateWeb();
app.MapWolverineEndpoints(options =>
{
    if (!CodeGeneration.IsGeneratingOpenApi()) options.WarmUpRoutes = RouteWarmup.Lazy;
    options.UseTemplateConventions();
});
app.MapDefaultEndpoints();
app.AssertTemplateRoutes();

// Also the command line: `migrate` (schema), `seed`, `codegen write`.
return await app.RunJasperFxCommands(args);

public partial class Program;
