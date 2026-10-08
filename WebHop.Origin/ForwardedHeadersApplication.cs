using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using WebHop.Core;

namespace WebHop.Origin
{
    /// <summary>
    /// Restores what the application would see if it faced the client directly: scheme, remote address
    /// and the gateway's request id. Only the gateway can write to a tunnel and it always overwrites
    /// these headers, so they are trusted here.
    /// </summary>
    internal sealed class ForwardedHeadersApplication<TContext>(IHttpApplication<TContext> application) : IHttpApplication<TContext>
        where TContext : notnull
    {
        public TContext CreateContext(IFeatureCollection features)
        {
            var request = features.Get<IHttpRequestFeature>();
            if (request is not null)
            {
                var headers = request.Headers;

                if (headers.TryGetValue(Headers.XForwardedProto, out var proto) && proto.Count > 0)
                    request.Scheme = proto[0]!;

                if (headers.TryGetValue(Headers.XForwardedFor, out var forwardedFor)
                    && IPEndPoint.TryParse(forwardedFor.ToString(), out var remote)
                    && features.Get<IHttpConnectionFeature>() is { } connection)
                {
                    connection.RemoteIpAddress = remote.Address;
                    connection.RemotePort = remote.Port;
                }

                if (headers.TryGetValue(Headers.XWebhopRequestId, out var requestId) && requestId.Count > 0
                    && features.Get<IHttpRequestIdentifierFeature>() is { } identifier)
                {
                    identifier.TraceIdentifier = requestId[0]!;
                }
            }

            return application.CreateContext(features);
        }

        public Task ProcessRequestAsync(TContext context) => application.ProcessRequestAsync(context);

        public void DisposeContext(TContext context, Exception? exception) => application.DisposeContext(context, exception);
    }
}
