namespace BccCode.Template.Api.Configuration;

public static class TemplateEnvironments
{
    public const string Test = "Test";

    public static bool IsDevelopmentOrTest(this IHostEnvironment environment)
    {
        return environment.IsDevelopment() || environment.IsEnvironment(Test);
    }
}
