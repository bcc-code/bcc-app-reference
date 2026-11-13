using System.Reflection;
using BccCode.Template.Api.Configuration;
using JasperFx.CommandLine;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>
/// Runs a JasperFx command of the API (<c>db-apply</c>, <c>db-assert</c>) in this process, through the API's entry point, the
/// same code as <c>dotnet run --project api/BccCode.Template.Api -- jasper db-apply</c>. The tests apply the schema this way, so
/// they run on the schema a real environment gets (standard 12.6).
/// </summary>
public static class SchemaCommands
{
    public static async Task<int> RunAsync(string command, string connectionString, IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var entryPoint = typeof(Program).Assembly.EntryPoint ?? throw new InvalidOperationException("The API assembly has no entry point.");
        // Development: Production needs pre-generated Wolverine code. The process does not look like a JasperFx command to the
        // configuration code (the process arguments are the test runner's), so the settings it needs are given too.
        // The settings are process-wide, which is fine because the integration tests run in one serial collection.
        var settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings__{EntityFrameworkConfiguration.ConnectionName}"] = connectionString,
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Internal__AllowAll"] = "true",
        };
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>()) settings[key] = value;
        var previous = settings.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        // The host must not start: it asserts that the schema exists, which db-assert is there to find out.
        var autoStartHost = JasperFxEnvironment.AutoStartHost;
        foreach (var (key, value) in settings) Environment.SetEnvironmentVariable(key, value);
        JasperFxEnvironment.AutoStartHost = false;
        try
        {
            var result = entryPoint.Invoke(null, [new[] { CodeGeneration.JasperFxPrefix, command }]);
            return result switch
            {
                Task<int> task => await task,
                int code => code,
                _ => throw new InvalidOperationException($"Unexpected entry point result {result?.GetType()}."),
            };
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
        finally
        {
            JasperFxEnvironment.AutoStartHost = autoStartHost;
            foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
