using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace BccCode.Template.Web.Tests;

public class OidcConfigurationTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static IHostEnvironment Environment(string name) => new HostingEnvironment { EnvironmentName = name };

    [Theory]
    [InlineData("Production")]
    [InlineData("Sandbox")]
    [InlineData("Preview")]
    public void A_deployed_environment_without_oidc_settings_fails_at_startup(string environment)
    {
        var act = () => AuthConfiguration.ValidateOidc(Settings(), Environment(environment));

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain("OIDC:Authority").And.Contain("OIDC:ClientId").And.Contain("OIDC:ClientSecret");
    }

    [Fact]
    public void Each_missing_setting_is_named()
    {
        var act = () => AuthConfiguration.ValidateOidc(
            Settings(("OIDC:Authority", "https://login.example"), ("OIDC:ClientId", "template"), ("OIDC:ClientSecret", " ")), Environment("Production"));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("OIDC:ClientSecret").And.NotContain("OIDC:Authority");
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
        var settings = Settings(("OIDC:Authority", "https://login.example"), ("OIDC:ClientId", "template"), ("OIDC:ClientSecret", "from-secret-manager"));

        var act = () => AuthConfiguration.ValidateOidc(settings, Environment("Production"));

        act.Should().NotThrow();
    }
}
