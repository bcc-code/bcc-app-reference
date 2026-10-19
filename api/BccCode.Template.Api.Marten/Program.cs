using System.Reflection;
using BccCode.Template.Api.Marten.Configuration;
using BccCode.Template.Api.Marten.Infrastructure;
using BccCode.Template.ServiceDefaults;
using BccCode.Template.Web;
using BccCode.Template.Web.Http;
using JasperFx;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Http.Marten;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTemplateWeb("Template API");
builder.AddTemplateMarten();
builder.AddTemplateWolverine();
builder.Services.AddTemplateLogs();

var app = builder.Build();

app.UseTemplateWeb();
app.MapTemplateWeb();
app.MapWolverineEndpoints(options =>
{
    if (!CodeGeneration.IsGeneratingOpenApi()) options.WarmUpRoutes = RouteWarmup.Lazy;
    options.UseTemplateConventions();
    // Two writers racing on one note: the second append answers 409 instead of 500. Only endpoints that declare a 409 get
    // the mapping, so the OpenAPI document does not advertise a conflict on endpoints that cannot have one.
    options.MapMartenConcurrencyFailuresToConflict(chain =>
        chain.Method.Method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(a => a.StatusCode == StatusCodes.Status409Conflict));
});
app.MapDefaultEndpoints();
app.AssertTemplateRoutes();

// Also the command line: `resources setup` (schema), `seed`, `codegen write`.
return await app.RunJasperFxCommands(args);

public partial class Program;
