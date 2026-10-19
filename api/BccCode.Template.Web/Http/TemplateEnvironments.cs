namespace BccCode.Template.Web.Http;

public static class TemplateEnvironments
{
    public const string Test = "Test";

    public static bool IsDevelopmentOrTest(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(Test);
}
