namespace BccCode.Template.Api.Marten.Seeding;

/// <summary>
/// Seeding production is the one failure here that nothing can undo, so the decision is an allowlist of disposable
/// environments, not a "not production" check: a misspelled or newly added environment name refuses instead of being
/// treated as safe.
/// </summary>
public static class SeedingGate
{
    private static readonly HashSet<string> Disposable = new(StringComparer.OrdinalIgnoreCase) { "Development", "Test", "Sandbox", "Preview" };

    public static bool IsAllowed(string environmentName) => Disposable.Contains(environmentName);
}
