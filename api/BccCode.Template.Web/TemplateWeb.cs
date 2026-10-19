using System.Text.Json.Serialization;
using BccCode.Template.Web.Auth;
using BccCode.Template.Web.Frontend;
using BccCode.Template.Web.Http;

namespace BccCode.Template.Web;

/// <summary>
/// The HTTP plumbing both persistence variants share. A variant's Program calls <c>AddTemplateWeb</c> and
/// <c>UseTemplateWeb</c> around its own store, Wolverine and endpoint registration, and supplies an
/// <see cref="IAuthSessionStorage"/> and a Data Protection key store.
/// </summary>
public static class TemplateWeb
{
    public static void AddTemplateWeb(this WebApplicationBuilder builder, string apiTitle)
    {
        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);   // the Server header tells an attacker the stack for free
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Strict numbers keep the OpenAPI document (and the generated client) at "integer" instead of "integer or string".
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        builder.AddTemplateForwardedHeaders();
        builder.AddTemplateAuth();
        builder.AddTemplateRateLimiting();
        builder.AddTemplateOpenApi(apiTitle);
        builder.AddTemplateFrontend();
    }

    /// <summary>Everything up to and including authorization, in the order that matters.</summary>
    public static void UseTemplateWeb(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();   // unexpected exceptions answer 500 with a problem and no exception text
        app.UseStatusCodePages();    // empty 401, 403 and 404 answers become problems too
        app.UseTemplateSecurityHeaders(app.Environment);
        app.UseTemplateStaticAssets();
        app.UseRouting();            // explicit, so the middleware below can read endpoint metadata
        app.UseAuthentication();
        app.UseTemplateRateLimiting();
        app.UseTemplateIndexHtml();
        app.UseAuthorization();
    }

    /// <summary>Maps the sign-in routes, the development OpenAPI document and the Vite proxy. Call before the variant's own endpoints.</summary>
    public static void MapTemplateWeb(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapTemplateOpenApi();
        app.MapTemplateFrontend();
    }

    /// <summary>The startup guard. Call last, after every endpoint is mapped.</summary>
    public static void AssertTemplateRoutes(this WebApplication app)
    {
        List<string> modeRoutes = [];
        if (app.Environment.IsDevelopment()) modeRoutes.Add(OpenApiConfiguration.DevelopmentDocumentRoute);
        if (app.Configuration.GetSection("Frontend").Get<FrontendOptions>()?.Mode == FrontendMode.Vite) modeRoutes.Add(FrontendConfiguration.ViteProxyRoute);
        app.AssertAnonymousRoutes([.. modeRoutes]);
    }
}
