using Microsoft.Playwright;
using OidcMock;

namespace BccCode.Template.E2eTests.Infrastructure;

[Collection(E2eSuite.Name)]
public abstract class E2eTest(AspireManager aspire) : IAsyncLifetime
{
    private readonly List<(IBrowserContext Context, IPage Page)> _pages = [];
    protected AspireManager Aspire => aspire;

    /// <summary>A fresh browser context per test. Tracing runs throughout and is kept only when the test fails.</summary>
    protected async Task<IPage> NewPageAsync()
    {
        var (_, page) = await NewContextAsync();
        return page;
    }

    /// <summary>A fresh browser context (its own cookies) with one page, for a test that needs several people or tabs.</summary>
    protected async Task<(IBrowserContext Context, IPage Page)> NewContextAsync()
    {
        var context = await aspire.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = aspire.ApiBase.ToString(),
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
            TimezoneId = "Europe/Oslo",
        });
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        // The app starts in Norwegian and remembers a choice in localStorage; the tests read English.
        await context.AddInitScriptAsync("try { localStorage.setItem('locale', 'en') } catch {}");
        var page = await context.NewPageAsync();
        _pages.Add((context, page));
        return (context, page);
    }

    /// <summary>A new page that is signed in as <paramref name="persona"/> and sits on <paramref name="path"/>.</summary>
    protected async Task<IPage> NewSignedInPageAsync(Persona persona, string path = "/notes")
    {
        var page = await NewPageAsync();
        await SignInAsync(page, persona, path);
        return page;
    }

    /// <summary>
    /// Opens <paramref name="path"/>, picks <paramref name="persona"/> on the mock's login page and waits until the app shows the
    /// page. The mock has one button per persona. An administrator is stepped up to a second factor by the app; the mock's
    /// persona either has one (amr mfa) or not, so no second page is shown for a persona that has it.
    /// </summary>
    protected async Task SignInAsync(IPage page, Persona persona, string path = "/notes")
    {
        await PickPersonaAsync(page, persona, path);
        await page.WaitForURLAsync(url => IsApp(url) && new Uri(url).AbsolutePath == path);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })).ToBeVisibleAsync();
    }

    /// <summary>Opens <paramref name="path"/> and picks <paramref name="persona"/> on the mock's login page; nothing after that.</summary>
    protected async Task PickPersonaAsync(IPage page, Persona persona, string path = "/notes")
    {
        await page.GotoAsync(path);
        await page.WaitForURLAsync(url => url.StartsWith(aspire.OidcBase.ToString(), StringComparison.Ordinal));
        await page.Locator($"button[name=\"persona\"][value=\"{persona.Key}\"]").ClickAsync();
    }

    protected bool IsApp(string url)
    {
        return url.StartsWith(aspire.ApiBase.ToString(), StringComparison.Ordinal);
    }

    protected bool IsOidc(string url)
    {
        return url.StartsWith(aspire.OidcBase.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A title no earlier run has used, because the database outlives a test run.</summary>
    protected static string UniqueTitle(string prefix)
    {
        return $"{prefix} {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000)}";
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        foreach (var (context, page) in _pages)
        {
            if (failed)
            {
                Directory.CreateDirectory(RepoPaths.Results);
                var name = $"{GetType().Name}-{Guid.NewGuid():N}";
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(RepoPaths.Results, $"{name}.png"), FullPage = true });
                await context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(RepoPaths.Results, $"{name}.zip") });
            }
            else
            {
                await context.Tracing.StopAsync(new TracingStopOptions());
            }
            await context.CloseAsync();
        }
    }
}
