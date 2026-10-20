namespace BccCode.Template.Web.Messaging;

/// <summary>The <c>Messaging</c> settings. The infrastructure sets them as environment variables (<c>Messaging__Topic</c>).</summary>
public sealed class MessagingOptions
{
    public const string Section = "Messaging";
    public const string ProjectIdKey = Section + ":PubsubProjectId";
    public const string TopicKey = Section + ":Topic";

    /// <summary>The topic every integration message is published to. Terraform creates it in GCP; the emulator setup creates it locally.</summary>
    public const string DefaultTopic = "template-messages";

    /// <summary>The GCP project that owns the topic. Required wherever the host runs; the command-line tools do not need it.</summary>
    public string? PubsubProjectId { get; set; }

    public string Topic { get; set; } = DefaultTopic;

    public static MessagingOptions From(IConfiguration configuration) =>
        configuration.GetSection(Section).Get<MessagingOptions>() ?? new MessagingOptions();
}
