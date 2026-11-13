/// <summary>The local stand-in for BCC Signon. Deployed only to previews.</summary>
internal static class OidcMockResources
{
    /// <summary>
    /// The mock registers its one client with this id. It is a public PKCE client, so there is no client secret anywhere, and the
    /// value is not a secret.
    /// </summary>
    public const string ClientId = "template";

    /// <summary>The OIDC mock with the persona picker, its client allowed to return to the API's two base URLs.</summary>
    public static IResourceBuilder<ProjectResource> AddOidcMock(this IDistributedApplicationBuilder builder, RunMode run,
        IResourceBuilder<ProjectResource> api)
    {
        var oidc = builder.AddTemplateProject<Projects.OidcMock>("oidc")
            .WithHttpsEndpoint(port: run.Port(LocalPorts.OidcMock), name: "https")
            .WithHttpHealthEndpoint()
            .WithEnvironment("Client__ClientId", ClientId)
            // The tests and the dashboard use localhost, the browser the template.dev.localhost name.
            .WithEnvironment("Client__Urls__0", api.GetEndpoint("https"))
            .WithEnvironment("Client__Urls__1", api.BrowserUrl());
        return oidc.WithUrls(context =>
        {
            var endpoint = oidc.GetEndpoint("https");
            context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://localhost:{endpoint.Port}/login", DisplayText = "Persona picker" });
        });
    }
}
