using BccCode.PubSub.Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Wolverine starts no listeners under <c>ServerlessWithOutbox</c>, but a BCC PubSub push (<c>/_bccpubsub/{topic}/...</c>) is
/// handed to the listener object of its endpoint and answers 503 without one. The listener only receives what the push
/// endpoint gives it: it polls nothing and holds no connection, so starting it costs no CPU between requests.
/// ServerlessWithOutbox also makes every endpoint durable after the endpoint policies have run, which would hand a pushed event to
/// an in-memory queue that runs after the response, when a request-billed instance has no CPU. The listener is therefore set to
/// inline just before it starts, so the handler runs inside the push request and BCC PubSub redelivers when it fails.
/// The template listens to no BCC PubSub topic yet, so this starts nothing until an app adds <c>ListenToBccPubSubTopic</c>.
/// </summary>
public sealed class BccPubSubListenerStarter(IWolverineRuntime runtime) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var transport = runtime.Options.Transports.GetOrCreate<BccPubSubTransport>();
        foreach (var endpoint in transport.Endpoints().OfType<BccPubSubEndpoint>().Where(e => e.IsListener))
        {
            endpoint.Mode = EndpointMode.Inline;
            await runtime.Endpoints.StartListenerAsync(endpoint, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
