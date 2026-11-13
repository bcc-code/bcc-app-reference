namespace BccCode.Template.TestData;

// Not top-level statements: those would add a second Program type next to the API's, which the integration tests reference.
public static class TestDataProgram
{
    public static async Task<int> Main(string[] args)
    {
        var builder = TestDataApp.CreateBuilder(args);
        var app = builder.Build();

        if (app.Configuration.GetValue<bool>("Seeder:Once"))
        {
            // The CI step of a PR preview: seed once, then exit. The web server is not started.
            await app.Services.GetRequiredService<TestDataSeeder>().SeedAsync(CancellationToken.None);
            return 0;
        }

        app.MapEndpoints();
        await app.RunAsync();
        return 0;
    }
}
