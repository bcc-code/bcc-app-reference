namespace BccCode.Template.Web.Messaging;

/// <summary>
/// Marks a message that leaves a handler or endpoint: it is stored in the outbox, published to the Pub/Sub topic and pushed
/// back to <c>/internal/pubsub/{subscription}</c>. Only these types are routed, and only these types the push receiver runs
/// (standard 13, rules 2 and 3). A message is a sealed record that carries ids and no copy of state (rule 1), and its handler
/// is idempotent (rule 8): delivery is at least once.
/// </summary>
public interface IIntegrationMessage;
