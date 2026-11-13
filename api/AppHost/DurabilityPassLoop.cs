using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Posts the API's durability pass every few seconds once the API is healthy. Failures are logged when they change.</summary>
internal sealed partial class DurabilityPassLoop(EndpointReference apiEndpoint, ResourceNotificationService notifications, ILogger<DurabilityPassLoop> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await notifications.WaitForResourceHealthyAsync(apiEndpoint.Resource.Name, stoppingToken);
            var url = $"{await apiEndpoint.GetValueAsync(stoppingToken)}/internal/durability/pass";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            string? last = null;
            using var timer = new PeriodicTimer(Interval);
            do
            {
                string outcome;
                try
                {
                    using var response = await client.PostAsync(url, null, stoppingToken);
                    outcome = response.IsSuccessStatusCode ? "ok" : $"HTTP {(int)response.StatusCode}";
                }
                catch (HttpRequestException e)
                {
                    outcome = e.Message;
                }

                if (outcome != last && (outcome != "ok" || last is not null))
                    LogOutcome(outcome);
                last = outcome;
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The AppHost is stopping.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Durability pass: {Outcome}")]
    private partial void LogOutcome(string outcome);
}
