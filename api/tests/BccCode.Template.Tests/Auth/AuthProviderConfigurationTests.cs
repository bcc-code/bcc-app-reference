using BccCode.Template.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace BccCode.Template.Tests.Auth;

/// <summary>Which providers are on and when the host refuses its configuration (ADR 0031). A JasperFx command skips the check.</summary>
public sealed class AuthProviderConfigurationTests
{
    private static readonly (string, string?)[] Signon =
        [("Oidc:Authority", "https://login.example"), ("Oidc:ClientId", "template"), ("Oidc:ClientSecret", "from-secret-manager")];

    private static readonly (string, string?)[] Entra =
        [("Entra:TenantId", "11111111-1111-4111-8111-111111111111"), ("Entra:ClientId", "entra-client"), ("Entra:ClientSecret", "from-secret-manager")];

    private static IConfiguration Settings(params (string Key, string? Value)[][] groups)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(groups.SelectMany(g => g).Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
    }

    private static IHostEnvironment Environment(string name)
    {
        return new HostingEnvironment { EnvironmentName = name };
    }

    private static (string, string?)[] Without(IEnumerable<(string, string?)> group, string key)
    {
        return [.. group.Where(v => v.Item1 != key)];
    }

    private static void Validate(IConfiguration settings, string environment = "Production")
    {
        AuthConfiguration.ValidateProviders(settings, Environment(environment));
    }

    [Fact]
    public void Signon_only_entra_only_and_both_pass_in_production()
    {
        ((Action)(() => Validate(Settings(Signon)))).Should().NotThrow();
        ((Action)(() => Validate(Settings(Entra)))).Should().NotThrow();
        ((Action)(() => Validate(Settings(Signon, Entra)))).Should().NotThrow();
    }

    [Fact]
    public void No_provider_at_all_fails_and_names_every_setting_to_choose_from()
    {
        var act = () => Validate(Settings());

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("Oidc:Authority").And.Contain("Oidc:ClientId").And.Contain("Oidc:ClientSecret")
            .And.Contain("Entra:TenantId").And.Contain("Entra:ClientId").And.Contain("Entra:ClientSecret");
    }

    [Theory]
    [InlineData("Oidc:Authority")]
    [InlineData("Oidc:ClientId")]
    [InlineData("Oidc:ClientSecret")]
    public void A_half_configured_signon_fails_and_names_the_missing_setting(string missing)
    {
        var act = () => Validate(Settings(Without(Signon, missing)));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(missing);
    }

    [Theory]
    [InlineData("Entra:TenantId")]
    [InlineData("Entra:ClientId")]
    [InlineData("Entra:ClientSecret")]
    public void A_half_configured_entra_fails_and_names_the_missing_setting(string missing)
    {
        var act = () => Validate(Settings(Without(Entra, missing)));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(missing);
    }

    [Fact]
    public void A_half_configured_provider_fails_even_when_the_other_one_is_complete()
    {
        ((Action)(() => Validate(Settings(Signon, Without(Entra, "Entra:ClientSecret"))))).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Entra:ClientSecret").And.NotContain("Oidc:");
        ((Action)(() => Validate(Settings(Without(Signon, "Oidc:ClientSecret"), Entra)))).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Oidc:ClientSecret").And.NotContain("Entra:");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Sandbox")]
    [InlineData("Preview")]
    public void An_entra_authority_override_is_refused_outside_development_and_test(string environment)
    {
        var settings = Settings(Entra, [("Entra:Authority", "https://login.example/tenant/v2.0")]);

        var act = () => Validate(settings, environment);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Entra:Authority");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Development_and_test_accept_an_entra_authority_override_and_no_secrets(string environment)
    {
        var settings = Settings(
            [("Entra:TenantId", "11111111-1111-4111-8111-111111111111"), ("Entra:ClientId", "template-entra"), ("Entra:Authority", "https://mock.invalid/")]);

        ((Action)(() => Validate(settings, environment))).Should().NotThrow();
        AuthConfiguration.EntraAuthority(settings, Environment(environment)).Should().Be("https://mock.invalid/");
    }

    [Fact]
    public void The_entra_authority_is_the_tenants_v2_endpoint_and_the_override_is_ignored_outside_development_and_test()
    {
        var settings = Settings(Entra, [("Entra:Authority", "https://evil.example/")]);

        AuthConfiguration.EntraAuthority(settings, Environment("Production")).Should()
            .Be("https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111/v2.0");
    }

    [Fact]
    public void Providers_are_on_by_their_settings_in_production()
    {
        var production = Environment("Production");

        EnabledProviders.From(Settings(Signon), production).Should().Be(new EnabledProviders(Signon: true, Entra: false));
        EnabledProviders.From(Settings(Entra), production).Should().Be(new EnabledProviders(Signon: false, Entra: true));
        EnabledProviders.From(Settings(Signon, Entra), production).Should().Be(new EnabledProviders(Signon: true, Entra: true));
        EnabledProviders.From(Settings(), production).Should().Be(new EnabledProviders(Signon: false, Entra: false));
    }

    [Fact]
    public void Development_turns_signon_on_by_its_authority_and_entra_by_tenant_and_client()
    {
        var development = Environment("Development");

        EnabledProviders.From(Settings([("Oidc:Authority", "https://oidc.invalid")]), development).Signon.Should().BeTrue();
        EnabledProviders.From(Settings([("Entra:TenantId", "t")]), development).Entra.Should().BeFalse("the tenant alone is half a provider");
        EnabledProviders.From(Settings([("Entra:TenantId", "t"), ("Entra:ClientId", "c")]), development).Entra.Should().BeTrue();
    }

    [Fact]
    public void Signon_is_not_on_without_a_client_id_in_production()
    {
        EnabledProviders.From(Settings([("Oidc:Authority", "https://login.example")]), Environment("Production")).Signon.Should().BeFalse();
    }

    [Fact]
    public void The_provider_keys_map_to_their_schemes_and_unknown_keys_to_none()
    {
        EnabledProviders.SchemeOf("bcc-signon").Should().Be("OpenIdConnect");
        EnabledProviders.SchemeOf("entra").Should().Be("Entra");
        EnabledProviders.SchemeOf("other").Should().BeNull();
        new EnabledProviders(Signon: true, Entra: false).Has("entra").Should().BeFalse();
        new EnabledProviders(Signon: true, Entra: true).Keys.Should().Equal("bcc-signon", "entra");
    }
}
