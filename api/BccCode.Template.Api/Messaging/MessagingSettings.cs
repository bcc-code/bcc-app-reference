namespace BccCode.Template.Api.Messaging;

/// <summary>The <c>Messaging</c> settings: whether internal messages use Google Pub/Sub, and then its project and topic.</summary>
public sealed record MessagingSettings(string ProjectId, string Topic)
{
    public const string Section = "Messaging";

    /// <summary>Messaging:UsePubSub, default false: internal messages then go through the durable local inbox and the durability pass.</summary>
    public static bool UsePubSub(IConfiguration configuration)
    {
        return configuration.GetValue<bool>($"{Section}:UsePubSub");
    }

    public static MessagingSettings Read(IConfiguration configuration)
    {
        string Get(string key)
        {
            return configuration[$"{Section}:{key}"]?.Trim() is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{Section}:{key} must be set when {Section}:UsePubSub is true.");
        }

        return new MessagingSettings(Get("ProjectId"), Get("Topic"));
    }
}

/// <summary>
/// The <c>BccPubSub</c> switch (standard 13, integration events): off by default, because the template has no BCC platform client
/// and no event of its own. When off, nothing of the BCC PubSub integration is configured or mapped.
/// </summary>
public static class BccPubSubSettings
{
    public const string Section = "BccPubSub";

    /// <summary>BccPubSub:Enabled, default false. Every place that skips BCC PubSub asks here.</summary>
    public static bool Enabled(IConfiguration configuration)
    {
        return configuration.GetValue<bool>($"{Section}:Enabled");
    }
}
