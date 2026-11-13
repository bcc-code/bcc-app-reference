using System.Text.Json.Serialization;
using BccCode.Template.Api.Auth;
using BccCode.Template.Api.Frontend;
using BccCode.Template.Api.Internal;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// The HTTP plumbing of the host: <c>Program</c> calls <c>AddTemplateWeb</c> and <c>UseTemplateWeb</c> around the store, Wolverine
/// and endpoint registration.
/// </summary>
public static class TemplateWeb
{
    public static void AddTemplateWeb(this WebApplicationBuilder builder, string apiTitle)
    {
        builder.AddTemplateSecurityHeaders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Strict numbers keep the OpenAPI document (and the generated client) at "integer" instead of "integer or string".
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        // ASPNETCORE_FORWARDEDHEADERS_ENABLED (set by Terraform behind the gateway) trusts X-Forwarded-For/-Proto from any sender,
        // so ingress must be limited to the load balancer. The gateway's header is "<supplied>,<client>,<load balancer>", so the
        // client is two hops from the right. Without the forwarded scheme the Secure cookies fail.
        builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardLimit = 2);
        builder.AddTemplateAuth();
        builder.AddTemplateInternalAuth();
        builder.AddTemplatePlatform();
        builder.AddTemplateOpenApi(apiTitle);
        builder.AddTemplateFrontend();
    }

    /// <summary>Everything up to and including authorization and the cross-origin check, in the order that matters.</summary>
    public static void UseTemplateWeb(this WebApplication app)
    {
        app.UseTemplateSecurityHeaders();
        // After the security headers, so error responses carry them. An unhandled exception becomes a 500 ProblemDetails without the message.
        app.UseExceptionHandler();
        // Before static files, so /index.html reaches the endpoint that renders it with the CSP nonce and the session check.
        app.UseRouting();
        app.UseTemplateStaticFiles();
        app.UseAuthentication();
        app.UseTemplateSessionTelemetry();
        app.UseTemplateAdminMfa();
        app.UseAuthorization();
        app.UseTemplateCrossOriginProtection();
    }

    /// <summary>Maps the sign-in routes, the development OpenAPI document and the frontend. Call after the Wolverine endpoints and the internal routes.</summary>
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
        app.AssertAnonymousRoutes([.. modeRoutes]);
        app.AssertInternalRoutes();
    }
}
