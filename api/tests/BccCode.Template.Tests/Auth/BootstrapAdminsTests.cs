using BccCode.Template.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

namespace BccCode.Template.Tests.Auth;

/// <summary><c>Auth:BootstrapAdmins</c> is one string of <c>&lt;provider&gt;:&lt;id&gt;</c> entries, so Secret Manager can hold it.</summary>
public sealed class BootstrapAdminsTests
{
    private const string OID = "e0000000-0000-4000-8000-000000000003";

    [Fact]
    public void Entries_are_split_on_commas_and_semicolons_and_trimmed()
    {
        var admins = BootstrapAdmins.Parse($"  entra:{OID} , bcc-signon:a0000000-0000-4000-8000-000000000003;entra:second ");

        admins.Should().Equal(
            new BootstrapAdmin("entra", OID),
            new BootstrapAdmin("bcc-signon", "a0000000-0000-4000-8000-000000000003"),
            new BootstrapAdmin("entra", "second"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" , ;; ")]
    public void An_empty_setting_names_nobody_and_is_valid(string? setting)
    {
        BootstrapAdmins.Parse(setting).Should().BeEmpty();
        BootstrapAdmins.Validate(setting).Should().BeNull();
    }

    [Theory]
    [InlineData("entra")]
    [InlineData("entra:")]
    [InlineData(":some-id")]
    [InlineData("google:some-id")]
    [InlineData("Entra:some-id")]
    [InlineData("entra:ok;broken")]
    public void A_malformed_entry_is_refused(string setting)
    {
        BootstrapAdmins.Validate(setting).Should().NotBeNull();
    }

    [Fact]
    public void A_malformed_entry_grants_nobody_and_does_not_hide_the_good_ones()
    {
        BootstrapAdmins.Parse("entra:ok;broken;google:x").Should().Equal(new BootstrapAdmin("entra", "ok"));
    }

    [Fact]
    public void The_message_names_the_setting_and_the_entry_but_never_a_value()
    {
        var message = BootstrapAdmins.Validate($"entra:{OID};secret-person-id");

        message.Should().Contain("Auth:BootstrapAdmins").And.Contain("entry 2").And.NotContain(OID).And.NotContain("secret-person-id");
    }

    [Fact]
    public void An_id_may_contain_colons()
    {
        BootstrapAdmins.Parse("entra:a:b").Should().Equal(new BootstrapAdmin("entra", "a:b"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Sandbox")]
    public void A_malformed_setting_stops_the_host_outside_development_and_test_without_naming_the_value(string environment)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oidc:Authority"] = "https://login.example", ["Oidc:ClientId"] = "template", ["Oidc:ClientSecret"] = "from-secret-manager",
            ["Auth:BootstrapAdmins"] = "facebook:private-value",
        }).Build();

        var act = () => AuthConfiguration.ValidateProviders(settings, new HostingEnvironment { EnvironmentName = environment });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Auth:BootstrapAdmins").And.NotContain("private-value");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void A_malformed_setting_is_left_to_the_parser_in_development_and_test(string environment)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:BootstrapAdmins"] = "broken" }).Build();

        var act = () => AuthConfiguration.ValidateProviders(settings, new HostingEnvironment { EnvironmentName = environment });

        act.Should().NotThrow();
    }
}
