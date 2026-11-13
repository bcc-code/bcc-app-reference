using System.Reflection;
using BccCode.Template.Api.Messaging;
using BccCode.Template.Messaging;
using BccCode.Template.Notes;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.Configuration;

namespace BccCode.Template.Tests.Messaging;

/// <summary>Standard 13: every message has the route its marker promises, and the Wolverine settings follow the decision (ADR 0024).</summary>
public sealed class MessagingConfigurationTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Messaging:ProjectId"] = "template-project",
        ["Messaging:Topic"] = "template-messages-test",
    };

    private static WolverineOptions Configure(string environment, bool usePubSub = true)
    {
        var settings = new Dictionary<string, string?>(Settings) { ["Messaging:UsePubSub"] = usePubSub ? "true" : "false" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new WolverineOptions();
        options.ConfigureTemplateMessaging(new FakeHostEnvironment(environment), configuration);
        return options;
    }

    private static Endpoint[] EndpointsFor(WolverineOptions options, Type messageType)
    {
        return options.Transports.AllEndpoints().Where(e => e.Subscriptions.Any(s => s.Matches(messageType))).ToArray();
    }

    private static IEnumerable<Type> InternalMessages()
    {
        return TemplateMessaging.TypesImplementing<IInternalMessage>();
    }

    [Fact]
    public void The_marker_scan_finds_the_messages_of_the_application()
    {
        InternalMessages().Should().Contain(typeof(NoteArchivedMessage));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void With_usepubsub_an_internal_message_and_its_fault_go_to_the_google_pubsub_topic_and_nowhere_else(string environment)
    {
        var options = Configure(environment);

        foreach (var type in InternalMessages().SelectMany(t => new[] { t, typeof(Fault<>).MakeGenericType(t) }))
        {
            var endpoint = EndpointsFor(options, type).Should().ContainSingle($"{type.Name} has one route").Subject;
            endpoint.Uri.Scheme.Should().Be("pubsub");
            endpoint.Uri.Segments.Last().Should().Be("template-messages-test");
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Without_usepubsub_there_is_no_google_pubsub_transport_and_internal_messages_stay_local(string environment)
    {
        var options = Configure(environment, usePubSub: false);

        options.Transports.AllEndpoints().Select(e => e.Uri.Scheme).Should().NotContain("pubsub");
        foreach (var type in InternalMessages()) EndpointsFor(options, type).Should().BeEmpty($"{type.Name} has no explicit route; conventional local routing applies");
    }

    [Fact]
    public void With_usepubsub_but_no_project_the_host_does_not_start()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Messaging:UsePubSub"] = "true" }).Build();

        var act = () => new WolverineOptions().ConfigureTemplateMessaging(new FakeHostEnvironment("Production"), configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Messaging:ProjectId*");
    }

    [Fact]
    public void The_template_has_no_integration_event_and_so_no_bcc_pubsub_route_while_it_is_off()
    {
        TemplateMessaging.TypesImplementing<IIntegrationEvent>().Should().BeEmpty("the template has none; an app adds its own");
        BccPubSubSettings.Enabled(new ConfigurationBuilder().Build()).Should().BeFalse("BCC PubSub is off until it is switched on");
        Configure("Production").Transports.AllEndpoints().Select(e => e.Uri.Scheme).Should().NotContain("bccpubsub");
    }

    [Fact]
    public void Every_internal_message_is_a_sealed_record_in_the_feature_project()
    {
        foreach (var type in InternalMessages())
        {
            type.IsSealed.Should().BeTrue(type.Name);
            type.GetMethod("<Clone>$").Should().NotBeNull($"{type.Name} is a record");
        }
    }

    [Theory]
    [InlineData("Production", AutoCreate.None)]
    [InlineData("Sandbox", AutoCreate.None)]
    [InlineData("Development", AutoCreate.CreateOrUpdate)]
    [InlineData("Test", AutoCreate.CreateOrUpdate)]
    public void Only_development_and_test_create_the_envelope_tables_at_startup(string environment, AutoCreate expected)
    {
        Configure(environment, usePubSub: false).AutoBuildMessageStorageOnStartup.Should().Be(expected);
    }

    [Fact]
    public void Durability_dead_letter_retention_the_execution_timeout_and_deduplication_follow_the_decision()
    {
        var options = Configure("Development");

        options.Durability.Mode.Should().Be(DurabilityMode.ServerlessWithOutbox);
        options.Durability.UseTransactionScopedDurabilityPassLock.Should().BeTrue();
        options.Durability.MessageDeduplicationMode.Should().Be(MessageDeduplicationMode.CompareByHash);
        options.Durability.DeadLetterQueueExpirationEnabled.Should().BeTrue();
        options.Durability.DeadLetterQueueExpiration.Should().Be(TimeSpan.FromDays(7));
        options.DefaultExecutionTimeout.Should().Be(TimeSpan.FromSeconds(60));
        options.Durability.EnableOutgoingTimestampIndex.Should().BeTrue();
    }

    [Fact]
    public void Handled_messages_of_the_feature_project_are_internal_messages_or_faults()
    {
        var handled = typeof(NoteArchivedMessage).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "Handle" or "HandleAsync"))
            .Select(method => method.GetParameters()[0].ParameterType)
            .ToList();

        handled.Should().Contain(typeof(NoteArchivedMessage));
        foreach (var message in handled)
        {
            var ok = typeof(IInternalMessage).IsAssignableFrom(message) || (message.IsGenericType && message.GetGenericTypeDefinition() == typeof(Fault<>));
            ok.Should().BeTrue($"{message.Name} is handled but is no internal message and no Fault<T>");
        }
    }
}
