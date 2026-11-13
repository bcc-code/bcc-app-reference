using Microsoft.Extensions.Hosting;
using NetArchTest.Rules;

namespace BccCode.Template.ArchitectureTests;

public class LayerRulesTests
{
    /// <summary>
    /// ADR 0025: the feature project holds the slices and what they share; what only the host uses (sign-in, the reverse proxy,
    /// Pub/Sub, KMS, hosting) stays in <c>BccCode.Template.Api</c>, so a slice cannot grow a dependency on it.
    /// </summary>
    [Fact]
    public void The_feature_project_does_not_depend_on_host_only_infrastructure()
    {
        Types.InAssembly(TemplateAssemblies.Features).ShouldNot()
            .HaveDependencyOnAny(
                "BccCode.Template.Api",
                "Yarp.ReverseProxy",
                "Microsoft.AspNetCore.Authentication",
                "Microsoft.IdentityModel",
                "Google.Cloud",
                "Wolverine.Pubsub",
                "Wolverine.Postgresql")
            .GetResult().FailingTypes.Select(t => t.FullName).Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Serverless style (no CPU between requests): nothing runs in the background. Messages are delivered by the outbox, Pub/Sub push
    /// and the durability pass (ADR 0024), all of them HTTP requests; a hosted service would only run while an instance is awake.
    /// The one exception starts the BCC PubSub listeners once, at startup: a listener polls nothing and only receives what the push
    /// endpoint hands it (<c>BccPubSubListenerStarter</c>).
    /// </summary>
    [Fact]
    public void Nothing_runs_in_the_background()
    {
        TemplateAssemblies.All.SelectMany(a => a.GetTypes()).Where(t => typeof(IHostedService).IsAssignableFrom(t))
            .Where(t => t.Name != "BccPubSubListenerStarter").Should().BeEmpty();
    }
}
