using System.Globalization;
using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace BccCode.Template.E2eTests.Infrastructure;

/// <summary>Runs axe-core on a page and fails with a readable list. Checks WCAG 2.1 A and AA and 2.2 AA (standard 19.13).</summary>
public static class Accessibility
{
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    public static async Task AssertNoViolationsAsync(IPage page, string state)
    {
        var options = new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] } };

        // Messages and dialogs fade in. Measured mid-fade, a colour is still partly transparent and fails the contrast rule, so the
        // scan waits until animations are done. A finding is confirmed by a second scan before it fails the test.
        AxeResult result = null!;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running' || a.effect?.getTiming().iterations === Infinity)");
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            result = await page.RunAxe(options);
            if (result.Violations.Length == 0) return;
        }

        var message = new StringBuilder().Append(CultureInfo.InvariantCulture, $"axe found {result.Violations.Length} accessibility violation(s) in \"{state}\" at {page.Url}:");
        foreach (var violation in result.Violations)
        {
            message.AppendLine().Append(CultureInfo.InvariantCulture, $"- {violation.Id} ({violation.Impact}): {violation.Help}. {violation.HelpUrl}");
            foreach (var node in violation.Nodes)
                message.AppendLine().Append(CultureInfo.InvariantCulture, $"    {node.Target}  {Truncate(node.Html)}");
        }

        throw new Xunit.Sdk.XunitException(message.ToString());
    }

    private static string Truncate(string? html)
    {
        return html is { Length: > 160 } ? html[..160] + "..." : html ?? "";
    }
}
