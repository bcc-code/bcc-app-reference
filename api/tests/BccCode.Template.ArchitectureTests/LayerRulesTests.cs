using Microsoft.Extensions.Hosting;
using NetArchTest.Rules;

namespace BccCode.Template.ArchitectureTests;

public class LayerRulesTests
{
    /// <summary>The web project is shared by every variant, so it cannot know about one: no Marten, no EF Core, no variant namespace.</summary>
    [Fact]
    public void The_web_project_does_not_depend_on_a_persistence_variant() =>
        Types.InAssemblies(TemplateAssemblies.Web).ShouldNot()
            .HaveDependencyOnAny("Marten", "Wolverine.Marten", "Microsoft.EntityFrameworkCore", "Npgsql", "BccCode.Template.Api")
            .GetResult().FailingTypes.Select(t => t.FullName).Should().BeNullOrEmpty();

    [Fact]
    public void A_variant_does_not_depend_on_another_variant()
    {
        foreach (var api in TemplateAssemblies.Apis)
        {
            var others = TemplateAssemblies.Apis.Where(a => a != api).Select(a => a.GetName().Name!).ToArray();
            if (others.Length == 0) continue;
            Types.InAssembly(api).ShouldNot().HaveDependencyOnAny(others).GetResult().FailingTypes.Select(t => t.FullName).Should().BeNullOrEmpty();
        }
    }

    /// <summary>
    /// Serverless style (no CPU between requests): nothing runs in the background. Messages are delivered by the outbox, Pub/Sub push
    /// and the durability pass (ADR 0007), all of them HTTP requests; a hosted service would only run while an instance is awake.
    /// </summary>
    [Fact]
    public void Nothing_runs_in_the_background() =>
        TemplateAssemblies.All.SelectMany(a => a.GetTypes()).Where(t => typeof(IHostedService).IsAssignableFrom(t)).Should().BeEmpty();
}
