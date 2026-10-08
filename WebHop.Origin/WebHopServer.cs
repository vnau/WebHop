using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebHop.Core;

namespace WebHop.Origin
{
    /// <summary>
    /// Serves the application through a WebHop gateway instead of a local port.
    /// The first configured URL (--urls / applicationUrl) is the gateway address.
    /// Internally this is Kestrel whose connections are tunnel streams opened to the gateway.
    /// </summary>
    public sealed class WebHopServer : IServer, IAsyncDisposable
    {
        private readonly WebHopServerOptions options;
        private readonly ILoggerFactory loggerFactory;
        private readonly bool ownsLoggerFactory;
        private WebHopConnectionListenerFactory? listenerFactory;
        private KestrelServer? kestrel;

        public WebHopServer() : this(new WebHopServerOptions())
        {
        }

        public WebHopServer(WebHopServerOptions options, ILoggerFactory? loggerFactory = null)
        {
            this.options = options;
            ownsLoggerFactory = loggerFactory is null;
            this.loggerFactory = loggerFactory ?? LoggerFactory.Create(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information));

            Features = new FeatureCollection();
            Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
        }

        public IFeatureCollection Features { get; }

        /// <summary>Identifies this origin to the gateway (X-Webhop-Origin-Id).</summary>
        public string OriginId => options.OriginId;

        /// <summary>Current state of the tunnels to the gateway.</summary>
        public WebHopOriginStatus Status =>
            listenerFactory?.Listener?.Status ?? new WebHopOriginStatus(WebHopConnectionState.Connecting, 0, options.MaxConnections, null);

        public async Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken ct) where TContext : notnull
        {
            var address = Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("No WebHop gateway URL configured. Pass it with --urls or applicationUrl.");
            var tunnelUri = GetTunnelUri(address, options.OriginId);

            // The gateway rejects every tunnel without one; fail now instead of retrying forever
            if (string.IsNullOrWhiteSpace(options.AuthToken))
                throw new InvalidOperationException(
                    $"No WebHop auth token configured. Set {Constants.AuthTokenEnvironmentVariable}, the {Constants.AuthTokenSetting} setting or WebHopServerOptions.AuthToken.");

            if (tunnelUri.Scheme == "ws" && !tunnelUri.IsLoopback)
                loggerFactory.CreateLogger<WebHopServer>().LogWarning("The WebHop auth token is sent unencrypted to {Gateway}; use an https:// gateway URL", tunnelUri.GetLeftPart(UriPartial.Authority));

            var kestrelOptions = new KestrelServerOptions
            {
                ApplicationServices = new ServiceCollection().AddSingleton(loggerFactory).BuildServiceProvider(),
            };
            kestrelOptions.Limits.MaxRequestBodySize = null;
            // Idle tunnel streams wait for the gateway; it closes pooled connections it no longer needs.
            // Not infinite: Kestrel then aborts every connection within a second (write data rate timeout)
            kestrelOptions.Limits.KeepAliveTimeout = TimeSpan.FromHours(1);
            kestrelOptions.Listen(new WebHopEndPoint(tunnelUri), listen => listen.Protocols = HttpProtocols.Http1);

            listenerFactory = new WebHopConnectionListenerFactory(options, loggerFactory);
            kestrel = new KestrelServer(Options.Create(kestrelOptions), listenerFactory, loggerFactory);

            await kestrel.StartAsync(new ForwardedHeadersApplication<TContext>(application), ct);
        }

        public Task StopAsync(CancellationToken ct)
        {
            return kestrel?.StopAsync(ct) ?? Task.CompletedTask;
        }

        public void Dispose()
        {
            kestrel?.Dispose();
            if (ownsLoggerFactory)
                loggerFactory.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>The configured gateway URL as the WebSocket URL tunnels connect to.</summary>
        internal static Uri GetTunnelUri(string address, string originId)
        {
            var configuredUrl = new Uri(address);
            var hubUrl = configuredUrl.AbsolutePath == "/" ? new Uri(configuredUrl, Constants.DefaultWebHopEndpoint) : configuredUrl;

            var builder = new UriBuilder(hubUrl)
            {
                Scheme = hubUrl.Scheme switch
                {
                    "https" or "wss" => "wss",
                    "http" or "ws" => "ws",
                    _ => throw new InvalidOperationException($"Unsupported WebHop gateway URL scheme: {hubUrl.Scheme}"),
                },
            };
            var query = builder.Query.TrimStart('?');
            builder.Query = (query.Length > 0 ? query + "&" : "") + $"{Constants.OriginIdParameter}={originId}";
            return builder.Uri;
        }
    }
}
