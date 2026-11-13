using BccCode.Template.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace BccCode.Template.Tests;

public class OidcConfigurationTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
    }

    private static IHostEnvironment Environment(string name)
    {
        return new HostingEnvironment { EnvironmentName = name };
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Sandbox")]
    [InlineData("Preview")]
    public void A_deployed_environment_without_oidc_settings_fails_at_startup(string environment)
    {
        var act = () => AuthConfiguration.ValidateOidc(Settings(), Environment(environment));

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain("Oidc:Authority").And.Contain("Oidc:ClientId").And.Contain("Oidc:ClientSecret");
    }

    [Fact]
    public void Each_missing_setting_is_named()
    {
        var act = () => AuthConfiguration.ValidateOidc(
            Settings(("Oidc:Authority", "https://login.example"), ("Oidc:ClientId", "template"), ("Oidc:ClientSecret", " ")), Environment("Production"));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Oidc:ClientSecret").And.NotContain("Oidc:Authority");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Development_and_test_need_no_settings(string environment)
    {
        var act = () => AuthConfiguration.ValidateOidc(Settings(), Environment(environment));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_complete_configuration_passes_in_production()
    {
        var settings = Settings(("Oidc:Authority", "https://login.example"), ("Oidc:ClientId", "template"), ("Oidc:ClientSecret", "from-secret-manager"));

        var act = () => AuthConfiguration.ValidateOidc(settings, Environment("Production"));

        act.Should().NotThrow();
    }
}
