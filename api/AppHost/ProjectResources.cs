using Aspire.Hosting.Dotnet;

/// <summary>What every .NET project in the topology has in common.</summary>
internal static class ProjectResources
{
    /// <summary>A project without its launch profile (the AppHost sets the endpoints), in the AppHost's environment.</summary>
    public static IResourceBuilder<DotnetProjectResource> AddTemplateProject(this IDistributedApplicationBuilder builder, string name,
        string projectPath)
    {
        return builder.AddDotnetProject(name, projectPath, options => options.ExcludeLaunchProfile = true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);
    }

    /// <summary>
    /// An http endpoint named <c>http</c> with <c>/health</c> as the health check. Health checks and the API's server-side calls use
    /// http, because CI runners do not trust the ASP.NET dev certificate; browsers use https.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> WithHttpHealthEndpoint(this IResourceBuilder<DotnetProjectResource> project)
    {
        return project.WithHttpEndpoint(name: "http").WithHttpHealthCheck("/health", endpointName: "http");
    }
}
