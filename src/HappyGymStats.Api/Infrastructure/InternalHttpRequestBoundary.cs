using System.Net;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Defines the transport boundary for HTTP actions that are callable only by a
/// process connecting directly to the API listener. A reverse proxy is not an
/// internal caller even when its upstream connection originates from 127.0.0.1.
/// </summary>
public static class InternalHttpRequestBoundary
{
    private static readonly string[] ProxyHeaders =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Real-IP",
    ];

    public static bool IsDirectInternalTransport(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
        {
            return false;
        }

        // Production and dev nginx set forwarding headers on every proxied API
        // request. A direct poller call does not. TestServer and non-IP local
        // transports may expose no RemoteIpAddress, so the absence of an IP is
        // accepted only when the request also has no proxy provenance headers.
        // This makes the current public proxy shape fail closed before the nginx
        // deny rule is deployed; the nginx rule then removes the route entirely.
        return ProxyHeaders.All(header => !httpContext.Request.Headers.ContainsKey(header));
    }
}
