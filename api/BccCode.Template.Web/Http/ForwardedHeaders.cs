using Microsoft.AspNetCore.HttpOverrides;

namespace BccCode.Template.Web.Http;

/// <summary>
/// TLS terminates at the load balancer, so Kestrel sees plain HTTP. Ingress is limited to that load balancer (Cloud Run
/// has no fixed proxy address to list), which is why every forwarder is trusted and <c>ForwardLimit</c> is 1: only the
/// header the load balancer appended counts. Without the forwarded scheme the Secure cookies and the antiforgery SSL
/// check fail. Do not expose the service to other ingress than that load balancer.
/// </summary>
public static class ForwardedHeaders
{
    public static void AddTemplateForwardedHeaders(this WebApplicationBuilder builder) =>
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                 | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });
}
