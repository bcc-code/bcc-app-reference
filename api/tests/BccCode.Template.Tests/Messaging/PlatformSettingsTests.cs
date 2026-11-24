using BccCode.Template.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace BccCode.Template.Tests.Messaging;

/// <summary>The BccPlatform settings are needed only for BCC PubSub, and are required outside Development and Test (standard 14.5).</summary>
public sealed class PlatformSettingsTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
    }

    [Theory]
    [InlineData("Production", "BccPlatform:Authority")]
    [InlineData("Sandbox", "BccPlatform:Authority")]
    public void A_deployed_environment_without_a_setting_stops_and_names_the_key(string environment, string key)
    {
        var act = () => PlatformSettings.Read(Settings(), new FakeHostEnvironment(environment));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Development_and_test_need_none_and_point_nowhere_real(string environment)
    {
        var settings = PlatformSettings.Read(Settings(), new FakeHostEnvironment(environment));

        settings.PubSubUrl.Should().Be("https://platform.invalid");
        settings.TokenEndpoint.Should().Be(PlatformSettings.DefaultTokenEndpoint);
    }

    [Fact]
    public void A_complete_configuration_is_read_as_given()
    {
        var settings = PlatformSettings.Read(
            Settings(("BccPlatform:Authority", "https://signon.example"), ("BccPlatform:Audience", "a"), ("BccPlatform:ClientId", "c"), ("BccPlatform:ClientSecret", "s"),
                ("BccPlatform:PubSubUrl", "https://pubsub.example"), ("BccPlatform:PublicBaseUrl", "https://app.example")),
            new FakeHostEnvironment("Production"));

        (settings.Authority, settings.PubSubUrl, settings.PublicBaseUrl).Should().Be(("https://signon.example", "https://pubsub.example", "https://app.example"));
    }
}
