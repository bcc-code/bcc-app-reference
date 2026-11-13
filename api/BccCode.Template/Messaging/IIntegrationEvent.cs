namespace BccCode.Template.Messaging;

/// <summary>
/// An event other services may consume, published to BCC PubSub. The type declares
/// <c>public const string EventType = "no.bcc.&lt;app&gt;.&lt;thing&gt;-&lt;happened&gt;.v1"</c>, which is its topic; a breaking
/// change adds a new version instead of changing the old one (standard 13.4 and 13.6). The template has none: BCC PubSub is off
/// until <c>BccPubSub:Enabled</c> is true, and the transport routes only the types that implement this interface.
/// </summary>
public interface IIntegrationEvent;
