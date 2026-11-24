using System.Diagnostics;
using BccCode.Template.Security;
using OpenTelemetry;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// Puts the identity provider's session id (<c>sid</c>) on the request's span and in the baggage of the calls it makes, so a trace
/// can be joined with the identity provider's and with the other BCC services' traces of the same sign-in (standard 22.5). It is
/// the identity provider's id, never our session cookie or store key, and baggage goes to BCC hosts only (BccBaggagePropagator).
/// A request without a signed-in session carries none.
/// </summary>
public static class SessionTelemetry
{
    /// <summary>The OpenTelemetry semantic convention, and the baggage key the other BCC services read (bcc-monitoring).</summary>
    public const string SessionIdKey = "session.id";

    /// <summary>After <c>UseAuthentication</c>, which reads the session.</summary>
    public static void UseTemplateSessionTelemetry(this WebApplication app)
    {
        app.Use((context, next) =>
        {
            if (context.User.FindFirst(AuthClaims.SessionId)?.Value is { Length: > 0 } sid)
            {
                Activity.Current?.SetTag(SessionIdKey, sid);
                Baggage.SetBaggage(SessionIdKey, sid);
            }

            return next(context);
        });
    }
}
