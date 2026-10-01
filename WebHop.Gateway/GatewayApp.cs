using System.Diagnostics;
using System.Net;
using Yarp.ReverseProxy.Forwarder;
using WebHop.Core;

namespace WebHop.Gateway
{
    public static class GatewayApp
    {
        public static WebApplication Create(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = null;
            });

            builder.Services.Configure<IISServerOptions>(options =>
            {
                options.MaxRequestBodySize = null;
            });

            builder.Services.AddHttpForwarder();
            builder.Services.AddSingleton<TunnelRegistry>();
            builder.Services.AddSingleton<TunnelAuthorization>();

            var app = builder.Build();

            var registry = app.Services.GetRequiredService<TunnelRegistry>();
            var authorization = app.Services.GetRequiredService<TunnelAuthorization>();
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayApp).FullName!);

            if (!authorization.IsConfigured)
                logger.LogError("No auth token: set {Variable} or the {Setting} setting. The gateway accepts no tunnels until then", Constants.AuthTokenEnvironmentVariable, Constants.AuthTokenSetting);

            // Every outgoing connection is an idle tunnel stream of the server named by the destination host
            var tunnelClient = new HttpMessageInvoker(new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                EnableMultipleHttp2Connections = true,
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
                ConnectCallback = (context, ct) => registry.TakeStreamAsync(context.DnsEndPoint.Host, ct),
            });

            var forwarderConfig = new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(120),
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

            app.MapGet(Constants.DefaultWebHopEndpoint + "/status", () =>
            {
                return "WebHop Gateway status";
            });

            // Servers open tunnel streams here: one WebSocket per HTTP connection
            app.Map(Constants.DefaultWebHopEndpoint, async context =>
            {
                string? serverId = context.Request.Query[Constants.ServerIdParameter];
                if (!context.WebSockets.IsWebSocketRequest || !TunnelRegistry.IsValidServerId(serverId))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!authorization.IsAuthorized(context.Request))
                {
                    if (authorization.IsConfigured)
                        logger.LogWarning("Rejected tunnel from {RemoteIp}: invalid auth token", context.Connection.RemoteIpAddress);
                    else
                        logger.LogWarning("Rejected tunnel from {RemoteIp}: the gateway has no auth token configured", context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                // Close tunnels as soon as the gateway stops; otherwise shutdown waits for each tunnel
                // request until the host's 30 s timeout, and servers reconnect that much later
                using var closing = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
                await registry.RunTunnelAsync(serverId!, new TunnelStream(webSocket), closing.Token);
            });

            // Explicit pattern: the default fallback pattern skips file paths (*.js, *.png, ...)
            app.MapFallback("{**path}", async (HttpContext context, IHttpForwarder forwarder) =>
            {
                var serverId = registry.PickServer();
                if (serverId is null)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }

                var error = await forwarder.SendAsync(context, $"http://{serverId}/", tunnelClient, forwarderConfig, new WebHopTransformer(serverId));
                if (error != ForwarderError.None)
                {
                    var exception = context.Features.Get<IForwarderErrorFeature>()?.Exception;
                    // Cancellations are the client going away, not a tunnel problem
                    var level = error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled
                        or ForwarderError.ResponseBodyCanceled or ForwarderError.UpgradeRequestCanceled or ForwarderError.UpgradeResponseCanceled
                        ? LogLevel.Debug : LogLevel.Warning;
                    logger.Log(level, "{ServerId} {RequestId} forwarding failed: {Error} {Message}", serverId, context.TraceIdentifier, error, exception?.Message);
                }
            });

            return app;
        }
    }
}
