namespace BccCode.Template.TestData;

/// <summary>Decides where the seeder may write. Every entry point that seeds or resets goes through it (standard 02.10).</summary>
public static class SeedingGate
{
    // Seeding or resetting a real environment cannot be undone, so the decision is an allowlist rather than a "not
    // production" check: a misspelled or newly added environment name refuses instead of being treated as safe.
    private static readonly HashSet<string> DISPOSABLE_ENVIRONMENTS = new(StringComparer.OrdinalIgnoreCase)
    {
        "Development", "dev", "sandbox", "preview",
    };

    public static bool IsAllowed(string? environmentName)
    {
        return !string.IsNullOrWhiteSpace(environmentName) && DISPOSABLE_ENVIRONMENTS.Contains(environmentName);
    }

    public static void EnsureAllowed(IHostEnvironment environment)
    {
        if (!IsAllowed(environment.EnvironmentName))
            throw new InvalidOperationException(
                $"Seeding and resetting data is refused in the environment '{environment.EnvironmentName}'. It is only allowed in: {string.Join(", ", DISPOSABLE_ENVIRONMENTS)}.");
    }
}
