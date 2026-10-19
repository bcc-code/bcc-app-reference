using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace BccCode.Template.Web.Http;

/// <summary>
/// CSRF check for JSON endpoints. ASP.NET's <c>UseAntiforgery</c> only checks form-bound endpoints, so Wolverine
/// endpoints get this as a <c>Before</c> middleware. Every unsafe request that carries the session cookie must send the
/// double-submit token in <c>X-XSRF-TOKEN</c>; an <c>Authorization</c> header never exempts it. A request without the
/// cookie is not an ambient browser request, so there is nothing to forge.
/// </summary>
public sealed class AntiforgeryMiddleware
{
    /// <summary>Tells the SPA the rejection was a missing or stale token: fetch a new one and retry once.</summary>
    public const string RefreshHeader = "X-Csrf-Refresh";

    public static async Task<IResult> BeforeAsync(HttpContext context, IAntiforgery antiforgery) =>
        await CheckAsync(context, antiforgery) ?? WolverineContinue.Result();

    /// <summary>The 400 problem when the request fails the check, or null when it may proceed.</summary>
    public static async Task<IResult?> CheckAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return null;
        if (!context.Request.Cookies.ContainsKey(Auth.AuthConfiguration.SessionCookieName))
            return null;
        if (await antiforgery.IsRequestValidAsync(context))
            return null;

        context.Response.Headers[RefreshHeader] = "1";
        return Results.Problem(Problems.Create(StatusCodes.Status400BadRequest, Problems.Keys.Csrf, "Invalid or missing antiforgery token"));
    }
}
