namespace BccCode.Template.Messaging;

/// <summary>
/// A command or event between our own handlers. A message that leaves a handler or endpoint is stored in the outbox and routed to
/// the Google Pub/Sub topic in GCP (pushed back to <c>/internal/pubsub/{subscription}</c>) and to the durable local inbox in
/// Development and Test. Only these types are routed, and only these types the push receiver runs (standard 13, rules 2 and 3).
/// A message is a sealed record that carries ids and no copy of state (rule 1), and its handler is idempotent (rule 8): delivery
/// is at least once. Its contract may change between deploys as long as one deploy stays compatible (standard 13.5).
/// </summary>
public interface IInternalMessage;
