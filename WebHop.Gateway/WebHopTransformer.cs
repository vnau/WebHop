using System.Net;
using Yarp.ReverseProxy.Forwarder;
using WebHop.Core;

namespace WebHop.Gateway
{
    /// <summary>
    /// Preserves the original Host and adds the forwarding and WebHop tracking headers.
    /// X-Forwarded-* are always overwritten, so servers can trust them.
    /// </summary>
    public sealed class WebHopTransformer(string serverId) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext context, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken ct)
        {
            await base.TransformRequestAsync(context, proxyRequest, destinationPrefix, ct);

            proxyRequest.Headers.Host = context.Request.Host.Value;

            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp is { IsIPv4MappedToIPv6: true })
                remoteIp = remoteIp.MapToIPv4();

            SetHeader(proxyRequest, Headers.XForwardedFor, remoteIp is null ? null : new IPEndPoint(remoteIp, context.Connection.RemotePort).ToString());
            SetHeader(proxyRequest, Headers.XForwardedProto, context.Request.Scheme);
            SetHeader(proxyRequest, Headers.XForwardedHost, context.Request.Host.Value);
            SetHeader(proxyRequest, Headers.XWebhopConnectionId, serverId);
            SetHeader(proxyRequest, Headers.XWebhopRequestId, context.TraceIdentifier);
        }

        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext context, HttpResponseMessage? proxyResponse, CancellationToken ct)
        {
            var result = await base.TransformResponseAsync(context, proxyResponse, ct);
            context.Response.Headers[Headers.XWebhopConnectionId] = serverId;
            context.Response.Headers[Headers.XWebhopRequestId] = context.TraceIdentifier;
            return result;
        }

        private static void SetHeader(HttpRequestMessage request, string name, string? value)
        {
            request.Headers.Remove(name);
            if (value is not null)
                request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
