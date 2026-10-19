using Microsoft.Extensions.FileProviders;

namespace BccCode.Template.Web.Frontend;

public static class FrontendConfiguration
{
    /// <summary>The routes the Vite proxy maps, for <see cref="Http.AnonymousRouteGuard"/>. The dev server serves source files that hold no data.</summary>
    public const string ViteProxyRoute = "/{**catch-all}";

    public static void AddTemplateFrontend(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Frontend");
        builder.Services.Configure<FrontendOptions>(section);
        var options = section.Get<FrontendOptions>() ?? new FrontendOptions();
        if (Http.CodeGeneration.IsGeneratingOpenApi()) return;   // the document build serves no pages

        if (options.Mode == FrontendMode.Vite)
        {
            builder.Services.AddHttpClient<ViteIndexHtmlTemplate>(c => c.BaseAddress = new Uri("https+http://frontend"));
            builder.Services.AddTransient<IIndexHtmlTemplate>(sp => sp.GetRequiredService<ViteIndexHtmlTemplate>());
            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
                .AddServiceDiscoveryDestinationResolver();
        }
        else
        {
            var path = options.StaticPath ?? throw new InvalidOperationException("Frontend:StaticPath is required when Frontend:Mode is Static.");
            builder.Services.AddSingleton<IIndexHtmlTemplate>(new FileIndexHtmlTemplate(Path.Combine(path, "index.html")));
        }
    }

    /// <summary>Static mode: the hashed files under /assets cache forever; index.html is never cached (see IndexHtmlMiddleware).</summary>
    public static void UseTemplateStaticAssets(this WebApplication app)
    {
        var options = app.Configuration.GetSection("Frontend").Get<FrontendOptions>() ?? new FrontendOptions();
        if (options.Mode != FrontendMode.Static || options.StaticPath is null) return;   // no path: the document build
        // Static files run before authentication. index.html must only come from IndexHtmlMiddleware, which requires a session
        // and adds the CSP nonce, so the file itself is never served from here.
        app.UseWhen(context => !IsIndexHtml(context.Request.Path), branch => branch.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.GetFullPath(options.StaticPath!)),
            OnPrepareResponse = ctx =>
            {
                if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        }));
    }

    // Case-insensitive: the file system of a developer machine may be, and the request must not reach the file by another spelling.
    private static bool IsIndexHtml(PathString path) => path.Equals("/index.html", StringComparison.OrdinalIgnoreCase);

    /// <summary>After authentication, before authorization: the shell redirects to sign-in itself.</summary>
    public static void UseTemplateIndexHtml(this WebApplication app) => app.UseMiddleware<IndexHtmlMiddleware>();

    public static void MapTemplateFrontend(this WebApplication app)
    {
        var options = app.Configuration.GetSection("Frontend").Get<FrontendOptions>() ?? new FrontendOptions();
        if (options.Mode == FrontendMode.Vite) app.MapReverseProxy();
    }
}
