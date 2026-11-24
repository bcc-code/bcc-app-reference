using BccCode.Template.ServiceDefaults;

namespace BccCode.Template.Api.Configuration;

public static class TemplateEnvironments
{
    public const string Test = "Test";

    public static bool IsDevelopmentOrTest(this IHostEnvironment environment)
    {
        return environment.IsDevelopment() || environment.IsEnvironment(Test);
    }

    /// <summary>
    /// Stops the host when Cloud Run runs it as Development or Test (a wrong <c>ASPNETCORE_ENVIRONMENT</c>). Every local
    /// relaxation is keyed on <see cref="IsDevelopmentOrTest"/>: <c>Internal:AllowAll</c> opens the <c>/internal/**</c>
    /// policies, OIDC metadata may come over HTTP and Wolverine changes its schema at startup. Cloud Run is recognised by
    /// <c>K_SERVICE</c>, which it always sets and no local host does.
    /// </summary>
    public static void AssertNotLocalInCloudRun(this IHostEnvironment environment, IConfiguration configuration)
    {
        if (GcpLoggingExtensions.IsCloudRun(configuration) && environment.IsDevelopmentOrTest())
            throw new InvalidOperationException(
                $"ASPNETCORE_ENVIRONMENT={environment.EnvironmentName} is not allowed in Cloud Run " +
                $"({GcpLoggingExtensions.CloudRunServiceVariable} is set).");
    }
}
