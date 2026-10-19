using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace BccCode.Template.Web.Http;

public static class RateLimitPolicies
{
    /// <summary>For anonymous routes: login, back-channel logout, antiforgery token. Per client IP.</summary>
    public const string Anonymous = "anonymous";
}

/// <summary>
/// Fixed one-minute windows, in memory: each instance counts on its own, so the effective limit is the setting
/// (<c>RateLimits:AnonymousPerMinute</c>, default 30) times the number of instances. The client IP is the one
/// <c>UseForwardedHeaders</c> restored from X-Forwarded-For; a caller who can set that header picks its own partition, so
/// the limit stops a client that does not try to avoid it, not a determined one. Routes opt in with
/// <c>RequireRateLimiting(RateLimitPolicies.Anonymous)</c>. The limiter is a singleton, so the container disposes it with
/// the host and its replenishment timer stops (the built-in <c>UseRateLimiter</c> never disposes the limiter it builds).
/// </summary>
public static class RateLimiting
{
    public const string AnonymousKey = "RateLimits:AnonymousPerMinute";
    public const int DefaultAnonymousPerMinute = 30;

    public static void AddTemplateRateLimiting(this WebApplicationBuilder builder)
    {
        var limit = builder.Configuration.GetValue<int?>(AnonymousKey) is > 0 and var configured ? configured : DefaultAnonymousPerMinute;
        builder.Services.AddSingleton(_ => PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName switch
            {
                null => RateLimitPartition.GetNoLimiter(""),
                RateLimitPolicies.Anonymous => RateLimitPartition.GetFixedWindowLimiter(
                    "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }),
                var name => throw new InvalidOperationException($"Unknown rate limiter policy '{name}'."),
            }));
    }

    /// <summary>After routing (it reads endpoint metadata) and before authorization, so a refused call costs nothing.</summary>
    public static void UseTemplateRateLimiting(this WebApplication app)
    {
        var limiter = app.Services.GetRequiredService<PartitionedRateLimiter<HttpContext>>();
        app.Use(async (context, next) =>
        {
            using var lease = limiter.AttemptAcquire(context);
            if (lease.IsAcquired)
            {
                await next(context);
                return;
            }

            var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(
                Problems.Create(StatusCodes.Status429TooManyRequests, Problems.Keys.TooManyRequests, "Too many requests"),
                options: null, contentType: "application/problem+json", context.RequestAborted);
        });
    }
}
