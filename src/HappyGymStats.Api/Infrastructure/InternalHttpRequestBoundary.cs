using System.Net;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Defines the transport boundary for HTTP actions that are callable only by a
/// process connecting directly to the API's loopback listener. A reverse proxy
/// is not an internal caller even when its upstream connection originates from
/// 127.0.0.1.
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

    public static bool IsDirectLoopback(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        {
            return false;
        }

        // Production and dev nginx set forwarding headers on every proxied API
        // request. A direct poller call does not. Rejecting the proxy shape here
        // means the application fails closed even before the nginx deny rule is
        // deployed, while the nginx rule removes the route from the public edge.
        return ProxyHeaders.All(header => !httpContext.Request.Headers.ContainsKey(header));
    }
}
