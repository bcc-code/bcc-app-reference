using System.Text.RegularExpressions;

namespace BccCode.Template.Web.Frontend;

public interface IIndexHtmlTemplate
{
    Task<string> GetAsync(CancellationToken ct);
}

public sealed class FileIndexHtmlTemplate(string path) : IIndexHtmlTemplate
{
    private readonly Lazy<Task<string>> _html = new(() => File.ReadAllTextAsync(path));

    public Task<string> GetAsync(CancellationToken ct) => _html.Value;
}

/// <summary>Development only. Vite's index.html includes the HMR client, so it is fetched on every request.</summary>
public sealed class ViteIndexHtmlTemplate(HttpClient client) : IIndexHtmlTemplate
{
    public Task<string> GetAsync(CancellationToken ct) => client.GetStringAsync("/", ct);
}

public static partial class IndexHtmlRenderer
{
    /// <summary>Puts this response's nonce on every script tag, so the CSP lets the app's own scripts run and nothing injected.</summary>
    public static string AddNonce(string template, string nonce) => ScriptWithoutNonce().Replace(template, $"<script nonce=\"{nonce}\"");

    [GeneratedRegex("<script(?![^>]*\\bnonce=)", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptWithoutNonce();
}
