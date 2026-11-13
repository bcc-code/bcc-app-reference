using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Security;
using JasperFx.CodeGeneration.Frames;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Http;
using Wolverine.Runtime;

namespace BccCode.Template.IntegrationTests.Hosting;

/// <summary>The Wolverine settings and the endpoint conventions, read from the running host.</summary>
public sealed class WolverineConfigurationTests(AppFixture fixture) : AppTest(fixture)
{
    private IWolverineRuntime Runtime => Fixture.Host.Services.GetRequiredService<IWolverineRuntime>();

    // The relay copies Identity.Name, the display name, into the envelope. Changes name the person by uid instead (ADR 0028).
    [Fact]
    public void The_user_name_relay_is_off()
    {
        Fixture.Host.Services.GetRequiredService<WolverineOptions>().EnableRelayOfUserName.Should().BeFalse();
    }

    [Fact]
    public void The_host_runs_serverless_with_the_outbox_and_the_settings_of_the_decision()
    {
        var options = Runtime.Options;

        options.Durability.Mode.Should().Be(DurabilityMode.ServerlessWithOutbox);
        options.Durability.UseTransactionScopedDurabilityPassLock.Should().BeTrue();
        options.Durability.MessageDeduplicationMode.Should().Be(MessageDeduplicationMode.CompareByHash);
        options.DefaultExecutionTimeout.Should().Be(TimeSpan.FromSeconds(60));
        options.Durability.DeadLetterQueueExpirationEnabled.Should().BeTrue();
        options.Durability.DeadLetterQueueExpiration.Should().Be(TimeSpan.FromDays(7));
        options.Durability.EnableOutgoingTimestampIndex.Should().BeTrue("the durability pass finds stale rows by it");
    }

    private List<HttpChain> Chains()
    {
        return Fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.Select(e => e.Metadata.GetMetadata<HttpChain>()).OfType<HttpChain>().ToList();
    }

    private static List<MethodCall> LoadCallsOf(HttpChain chain)
    {
        return chain.Middleware.OfType<MethodCall>().Where(m => m.Method.DeclaringType == typeof(CallerAccess) && m.Method.Name == nameof(CallerAccess.Load)).ToList();
    }

    // CallerAccessParameterStrategy: one call however many methods take the parameter. Read from the chains, not the generated
    // source, which Development compiles in memory and Production reads from the gitignored Internal/Generated.
    [Fact]
    public void A_chain_loads_CallerAccess_at_most_once()
    {
        Chains().Where(c => LoadCallsOf(c).Count > 1).Select(c => c.Method.HandlerType.Name).Should().BeEmpty();
    }

    [Fact]
    public void Permission_and_the_endpoint_share_the_one_CallerAccess_load()
    {
        var chain = Chains().Single(c => c.Method.HandlerType.Name == "EditNoteEndpoint");

        var load = LoadCallsOf(chain).Should().ContainSingle().Which;
        var calls = chain.Middleware.OfType<MethodCall>().Append(chain.Method).Where(m => m.Method.GetParameters().Any(p => p.ParameterType == typeof(CallerAccess))).ToList();
        calls.Select(m => m.Method.Name).Should().BeEquivalentTo("Permission");
        calls.Should().OnlyContain(m => m.Arguments.Contains(load.ReturnVariable));
    }

    [Fact]
    public void Permission_runs_after_the_load_and_before_Before_and_Validate()
    {
        var names = Chains().Single(c => c.Method.HandlerType.Name == "EditNoteEndpoint").Middleware.OfType<MethodCall>().Select(m => m.Method.Name).ToList();

        names.IndexOf("LoadAsync").Should().BeLessThan(names.IndexOf("Permission"));
        names.IndexOf("Permission").Should().BeLessThan(names.IndexOf("Before"));
        names.IndexOf("Before").Should().BeLessThan(names.IndexOf("Validate"));
    }
}
