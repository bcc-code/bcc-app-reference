/// <summary>What every .NET project in the topology has in common.</summary>
internal static class ProjectResources
{
    /// <summary>A project without its launch profile (the AppHost sets the endpoints), in the AppHost's environment.</summary>
    public static IResourceBuilder<ProjectResource> AddTemplateProject<TProject>(this IDistributedApplicationBuilder builder, string name)
        where TProject : IProjectMetadata, new()
    {
        return builder.AddProject<TProject>(name, launchProfileName: null)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);
    }

    /// <summary>
    /// An http endpoint named <c>http</c> with <c>/health</c> as the health check. Health checks and the API's server-side calls use
    /// http, because CI runners do not trust the ASP.NET dev certificate; browsers use https.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithHttpHealthEndpoint(this IResourceBuilder<ProjectResource> project)
    {
        return project.WithHttpEndpoint(name: "http").WithHttpHealthCheck("/health", endpointName: "http");
    }
}
