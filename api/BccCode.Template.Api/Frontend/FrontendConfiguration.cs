using BccCode.Template.Api.Auth;
using BccCode.Template.Api.Configuration;
using Microsoft.Extensions.FileProviders;

namespace BccCode.Template.Api.Frontend;

public enum FrontendMode
{
    /// <summary>Development: everything that is not an API route is proxied to the Vite dev server (YARP), behind the user policy.</summary>
    Vite,

    /// <summary>Tests, E2E and the container image: the built frontend is served from StaticPath (default: wwwroot).</summary>
    Static,
}

public sealed class FrontendOptions
{
    public FrontendMode Mode { get; set; } = FrontendMode.Static;

    /// <summary>The folder with the built frontend (<c>frontend/dist</c>, or <c>wwwroot</c> in the image).</summary>
    public string? StaticPath { get; set; }
}

public static class FrontendConfiguration
{
    private const string IMMUTABLE = "public, max-age=31536000, immutable";

    private static FrontendOptions Read(IConfiguration configuration)
    {
        return configuration.GetSection("Frontend").Get<FrontendOptions>() ?? new FrontendOptions();
    }

    internal static bool IsVite(IConfiguration configuration)
    {
        return Read(configuration).Mode == FrontendMode.Vite;
    }

    /// <summary>
    /// Vite content-hashes everything under /assets, so those files are immutable. index.html is not a static file: it is
    /// rendered per request with a CSP nonce and must never be cached (<see cref="IndexHtml"/>). In Cloud Run the gateway
    /// sends /assets to a bucket; the image still contains them, so the run.app URL and a local container work without
    /// the gateway.
    /// </summary>
    private static StaticFileOptions StaticOptions(PhysicalFileProvider files)
    {
        return new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context =>
            {
                if (context.Context.Request.Path.StartsWithSegments("/assets"))
                    context.Context.Response.Headers.CacheControl = IMMUTABLE;
            },
        };
    }

    private static string Root(IHostEnvironment environment, FrontendOptions options)
    {
        return options.StaticPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    }

    public static void AddTemplateFrontend(this WebApplicationBuilder builder)
    {
        if (CodeGeneration.IsJasperFxCommand() || Read(builder.Configuration).Mode != FrontendMode.Vite) return;
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();
    }

    /// <summary>Static mode only. After routing and before the endpoints, so built assets are served directly and index.html is not.</summary>
    public static void UseTemplateStaticFiles(this WebApplication app)
    {
        // A JasperFx command serves no pages and runs without a built frontend.
        if (CodeGeneration.IsJasperFxCommand()) return;
        var options = Read(app.Configuration);
        if (options.Mode != FrontendMode.Static) return;
        app.UseStaticFiles(StaticOptions(new PhysicalFileProvider(Path.GetFullPath(Root(app.Environment, options)))));
    }

    public static void MapTemplateFrontend(this WebApplication app)
    {
        if (CodeGeneration.IsJasperFxCommand()) return;
        // A literal /api segment outranks the frontend catch-alls, so a mistyped API path gets a 404 ProblemDetails
        // instead of index.html from Vite or the static fallback.
        app.Map("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound))
            .RequireAuthorization(TemplatePolicies.User)
            .ExcludeFromDescription();
        var options = Read(app.Configuration);
        if (options.Mode == FrontendMode.Vite)
        {
            // No authorization on the route: it falls to the fallback policy, so a signed-out person is sent to sign in
            // before Vite serves anything (appsettings.Development.json).
            app.MapReverseProxy();
            return;
        }

        // "/", "/index.html" and client-side routes all get the shell. The fallback only matches paths without a file
        // extension, so a missing asset is a 404 and not index.html. Both are under the fallback policy.
        var index = new IndexHtml(Path.Combine(Root(app.Environment, options), "index.html"));
        app.MapMethods("/index.html", [HttpMethods.Get, HttpMethods.Head], (HttpContext context) => index.WriteAsync(context));
        app.MapFallback((HttpContext context) => index.WriteAsync(context));
    }
}
