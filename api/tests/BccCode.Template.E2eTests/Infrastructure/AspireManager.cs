using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace BccCode.Template.E2eTests.Infrastructure;

/// <summary>
/// One AppHost (E2E=true) shared by every test: Postgres, the schema step, the seeder and the API serving the built frontend.
/// Tests start only after the data is seeded and the API answers (standard 19.6).
/// </summary>
public sealed class AspireManager : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = Environment.GetEnvironmentVariable("CI") is null ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(8);
    private IPlaywright? _playwright;

    public DistributedApplication App { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    /// <summary>The https endpoint, one origin as in production. The browser ignores the dev certificate error.</summary>
    public Uri ApiBase => App.GetEndpoint("api", "https");
    /// <summary>The OIDC mock over http, the address the API uses as its authority under E2E and so the one the browser is sent to.</summary>
    public Uri OidcBase => App.GetEndpoint("oidc", "http");

    public async ValueTask InitializeAsync()
    {
        var index = Path.Combine(RepoPaths.FrontendDist, "index.html");
        if (!File.Exists(index))
            throw new InvalidOperationException($"The frontend is not built: run pnpm --dir frontend build (missing {index}).");

        Assertions.SetDefaultExpectTimeout(10_000);
        _playwright = await Playwright.CreateAsync();
        // E2E_BROWSER_CHANNEL (chrome, msedge) uses an installed browser instead of the one `playwright install` downloads.
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !Debugger.IsAttached,
            Channel = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL"),
        });

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(["--E2E=true"]).WaitAsync(Timeout);
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        App = await builder.BuildAsync().WaitAsync(Timeout);
        await App.StartAsync().WaitAsync(Timeout);

        await WaitUntilReadyAsync();
    }

    // "Running" is not "ready": the AppHost's health checks (/health) pass for the seeder only after the seed has committed
    // (the test data is applied before it accepts requests), and for the API once it answers.
    private async Task WaitUntilReadyAsync()
    {
        var notifications = App.ResourceNotifications;
        await notifications.WaitForResourceAsync("schema", KnownResourceStates.Finished).WaitAsync(Timeout);
        foreach (var resource in new[] { "oidc", "testdata", "api" })
            await notifications.WaitForResourceHealthyAsync(resource).WaitAsync(Timeout);
    }

    // Chrome takes seconds to exit and the AppHost longer to stop its resources. Neither needs the other, so they stop
    // side by side.
    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(CloseBrowserAsync(), StopAppAsync());
    }

    private async Task CloseBrowserAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
    }

    private async Task StopAppAsync()
    {
        if (App is not null) await App.DisposeAsync();
    }
}
