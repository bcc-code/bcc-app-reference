using Aspire.Hosting.Dotnet;

/// <summary>The local stand-in for BCC Signon. Deployed only to previews.</summary>
internal static class OidcMockResources
{
    /// <summary>
    /// The mock registers its one client with this id. It is a public PKCE client, so there is no client secret anywhere, and the
    /// value is not a secret.
    /// </summary>
    public const string ClientId = "template";

    /// <summary>The OIDC mock with the persona picker, its client allowed to return to the API's two base URLs.</summary>
    public static IResourceBuilder<DotnetProjectResource> AddOidcMock(this IDistributedApplicationBuilder builder, RunMode run,
        IResourceBuilder<DotnetProjectResource> api, IResourceBuilder<PostgresDatabaseResource> database)
    {
        var oidc = builder.AddTemplateProject("oidc", "../OidcMock/OidcMock.csproj")
            .WithHttpsEndpoint(port: run.Port(LocalPorts.OidcMock), name: "https")
            // The signing keys live in the app's database (schema oidcmock), so a restart keeps the JWKS the API has cached.
            .WithReference(database)
            .WaitFor(database)
            .WithHttpHealthEndpoint()
            .WithEnvironment("Client__ClientId", ClientId)
            // The tests and the dashboard use localhost, the browser the instance's *.dev.localhost name (RunMode.BrowserHost).
            .WithEnvironment("Client__Urls__0", api.GetEndpoint("https"))
            .WithEnvironment("Client__Urls__1", api.BrowserUrl(run))
            // The mock is always reached on localhost, so parallel instances share its host; a cookie name per instance keeps
            // their sign-ins apart (RunMode.CookieSuffix, empty for the sole instance).
            .WithEnvironment("Session__CookieSuffix", run.CookieSuffix);
        return oidc.WithUrls(context =>
        {
            var endpoint = oidc.GetEndpoint("https");
            context.Urls.Add(new ResourceUrlAnnotation { Url = $"https://localhost:{endpoint.Port}/login", DisplayText = "Persona picker" });
        });
    }
}
