using BccCode.Template.Api.Configuration;
using BccCode.Template.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BccCode.Template.Tests.Configuration;

/// <summary>
/// Development and Test unlock the same relaxations (<c>Internal:AllowAll</c>, OIDC metadata over HTTP, schema changes at
/// startup), so Cloud Run must refuse both.
/// </summary>
public class CloudRunEnvironmentTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData(TemplateEnvironments.Test)]
    public void Local_environments_in_Cloud_Run_refuse_to_start(string environment)
    {
        FluentActions.Invoking(() => Env(environment).AssertNotLocalInCloudRun(Config(cloudRunService: "template-api")))
            .Should().Throw<InvalidOperationException>().WithMessage($"*{environment}*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Sandbox")]
    public void Deployed_environments_in_Cloud_Run_start(string environment)
    {
        FluentActions.Invoking(() => Env(environment).AssertNotLocalInCloudRun(Config(cloudRunService: "template-api")))
            .Should().NotThrow();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData(TemplateEnvironments.Test)]
    public void Local_environments_outside_Cloud_Run_start(string environment)
    {
        FluentActions.Invoking(() => Env(environment).AssertNotLocalInCloudRun(Config(cloudRunService: null)))
            .Should().NotThrow();
    }

    private static IConfiguration Config(string? cloudRunService)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection([new(GcpLoggingExtensions.CloudRunServiceVariable, cloudRunService)])
            .Build();
    }

    private static IHostEnvironment Env(string name)
    {
        return new TestEnvironment(name);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BccCode.Template.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
