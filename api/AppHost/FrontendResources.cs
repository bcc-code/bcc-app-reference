/// <summary>Where the API gets the frontend from: the Vite dev server, or a built <c>frontend/dist</c> in E2E runs.</summary>
internal static class FrontendResources
{
    /// <summary>
    /// The Vite dev server behind the API: the API proxies it, so the browser talks to one origin. Vite answers <c>/api</c> with
    /// index.html too, so a page opened on Vite's own port breaks; vite.config.ts sends such a browser on to the API's address, and
    /// the dashboard does not list the port.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithViteFrontend(this IResourceBuilder<ProjectResource> api, RunMode run)
    {
        var frontend = api.ApplicationBuilder.AddViteApp("frontend", "../../frontend")
            .WithPnpm()
            .WithParentRelationship(api)
            .WithEnvironment("TEMPLATE_APP_URL", api.BrowserUrl())
            .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);
        if (run.IsSoleInstance)
            frontend.WithEndpoint("http", endpoint => endpoint.Port = LocalPorts.Vite);
        return api.WithReference(frontend).WithEnvironment("Frontend__Mode", "Vite");
    }

    /// <summary>The built frontend served by the API, for E2E runs. <c>Frontend:StaticPath</c> overrides <c>frontend/dist</c>.</summary>
    public static IResourceBuilder<ProjectResource> WithBuiltFrontend(this IResourceBuilder<ProjectResource> api)
    {
        var builder = api.ApplicationBuilder;
        var staticPath = builder.Configuration["Frontend:StaticPath"]
                         ?? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../frontend/dist"));
        return api.WithEnvironment("Frontend__Mode", "Static").WithEnvironment("Frontend__StaticPath", staticPath);
    }
}
