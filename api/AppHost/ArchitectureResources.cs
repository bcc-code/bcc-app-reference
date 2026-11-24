/// <summary>The Structurizr viewer for the C4 diagrams.</summary>
internal static class ArchitectureResources
{
    /// <summary>
    /// C4 diagrams from docs/architecture/workspace.dsl; refresh the browser after editing the file. The image is pinned in that
    /// folder's Dockerfile (stage <c>structurizr</c>). Not in E2E runs (standard 02.13).
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddArchitectureViewer(this IDistributedApplicationBuilder builder, RunMode run)
    {
        var architecture = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../docs/architecture"));
        return builder.AddDockerfile("architecture", architecture, stage: "structurizr")
            .WithBindMount(architecture, "/usr/local/structurizr")
            .WithArgs("local")
            .WithHttpEndpoint(port: run.Port(LocalPorts.Architecture), targetPort: 8080)
            .WithUrlForEndpoint("http", url =>
            {
                url.Url = "/workspace/1/diagrams";
                url.DisplayText = "C4 diagrams";
            });
    }
}
