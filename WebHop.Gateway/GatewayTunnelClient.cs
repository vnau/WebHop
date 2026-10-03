using System.Diagnostics;
using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace WebHop.Gateway
{
    /// <summary>
    /// The forwarder's HTTP client and request config. Every outgoing connection is resolved to an
    /// idle tunnel stream of the server named by the destination host, instead of dialing a socket.
    /// A singleton: it holds one pooled <see cref="HttpMessageInvoker"/> shared by all requests.
    /// </summary>
    internal sealed class GatewayTunnelClient(TunnelRegistry registry) : IDisposable
    {
        public HttpMessageInvoker Invoker { get; } = new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectCallback = (context, ct) => registry.TakeStreamAsync(context.DnsEndPoint.Host, ct),
        });

        public ForwarderRequestConfig Config { get; } = new()
        {
            ActivityTimeout = TimeSpan.FromSeconds(120),
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        public void Dispose() => Invoker.Dispose();
    }
}
