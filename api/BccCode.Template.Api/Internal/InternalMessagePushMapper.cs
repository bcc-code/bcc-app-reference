using System.Reflection;
using BccCode.Template.Messaging;
using Wolverine;
using Wolverine.Http.Transport.Push;
using Wolverine.Util;

namespace BccCode.Template.Api.Internal;

/// <summary>
/// The Pub/Sub push mapper, limited to <see cref="IInternalMessage"/> types. Wolverine's push receiver runs any message type
/// that has a handler, so without this a publisher on our topic could run a message that is meant for inline use only. The
/// message type comes from the envelope and is resolved against this list, never against a name the sender chooses (standard 13,
/// rule 3). Every envelope is checked, also each one of a binary Wolverine batch. A request with any other type is refused whole
/// (400), so Pub/Sub dead-letters it.
/// </summary>
public sealed partial class InternalMessagePushMapper(IPushEnvelopeMapper inner, IReadOnlySet<string> allowed, ILogger<InternalMessagePushMapper> logger)
    : IPushEnvelopeMapper
{
    /// <summary>The Wolverine message type names of the concrete <see cref="IInternalMessage"/> types in the assemblies.</summary>
    public static IReadOnlySet<string> TypesIn(params Assembly[] assemblies)
    {
        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IInternalMessage).IsAssignableFrom(type))
            .Select(type => type.ToMessageTypeName())
            .ToHashSet(StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyList<Envelope>> MapAsync(HttpContext context, CancellationToken cancellation)
    {
        var envelopes = await inner.MapAsync(context, cancellation);
        // Logs the envelope id and type, never the body.
        if (envelopes.FirstOrDefault(envelope => envelope.MessageType is null || !allowed.Contains(envelope.MessageType)) is { } refused)
        {
            Refused(logger, refused.Id, envelopes.Count, refused.MessageType);
            return [];
        }
        return envelopes;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refused a Pub/Sub push: envelope {EnvelopeId} of {EnvelopeCount} is {MessageType}, not an internal message")]
    private static partial void Refused(ILogger logger, Guid envelopeId, int envelopeCount, string? messageType);
}
