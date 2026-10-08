using Yarp.ReverseProxy.Forwarder;
using WebHop.Core;
using WebHop.Gateway;

// Same namespace as UseWebSockets, so UseWebHopGateway() needs no extra using
namespace Microsoft.AspNetCore.Builder
{
    public static class GatewayApplicationBuilderExtensions
    {
        /// <summary>
        /// Routes traffic through the WebHop gateway. Origins open tunnels at
        /// <see cref="Constants.DefaultWebHopEndpoint"/>; every other request is forwarded to a
        /// connected origin round-robin. When no origin is connected the request falls through to
        /// the next middleware, so a host can serve its own content (or end the pipeline with a 503).
        /// Call <c>AddWebHopGateway()</c> first and <c>UseWebSockets()</c> before this.
        /// </summary>
        public static IApplicationBuilder UseWebHopGateway(this IApplicationBuilder app)
        {
            var services = app.ApplicationServices;
            var registry = services.GetRequiredService<TunnelRegistry>();
            var authorization = services.GetRequiredService<TunnelAuthorization>();
            var forwarder = services.GetRequiredService<IHttpForwarder>();
            var tunnelClient = services.GetRequiredService<GatewayTunnelClient>();
            var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayApplicationBuilderExtensions).Namespace!);

            if (!authorization.IsConfigured)
                logger.LogError("No auth token: set {Variable} or the {Setting} setting. The gateway accepts no tunnels until then", Constants.AuthTokenEnvironmentVariable, Constants.AuthTokenSetting);

            // How many tunnels one origin may keep open, from the host's WebSocket budget (0 = unlimited)
            var maxConnectionsPerOrigin = WebSocketBudget.PerOriginCap(services.GetRequiredService<IConfiguration>());
            if (maxConnectionsPerOrigin > 0)
                logger.LogInformation("Advertising up to {Max} tunnels per origin (host WebSocket budget)", maxConnectionsPerOrigin);

            return app.Use(async (context, next) =>
            {
                var path = context.Request.Path;

                if (path.Equals(Constants.DefaultWebHopEndpoint + "/status", StringComparison.OrdinalIgnoreCase))
                {
                    await context.Response.WriteAsync("WebHop Gateway status");
                    return;
                }

                // Origins open tunnel streams here: one WebSocket per HTTP connection
                if (path.Equals(Constants.DefaultWebHopEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    await AcceptTunnelAsync(context, registry, authorization, lifetime, logger, maxConnectionsPerOrigin);
                    return;
                }

                // Public traffic: forward to a connected origin, or fall through when none is connected
                var originId = registry.PickOrigin();
                if (originId is null)
                {
                    await next(context);
                    return;
                }

                var error = await forwarder.SendAsync(context, $"http://{originId}/", tunnelClient.Invoker, tunnelClient.Config, new WebHopTransformer(originId));
                if (error != ForwarderError.None)
                {
                    var exception = context.Features.Get<IForwarderErrorFeature>()?.Exception;
                    // Cancellations are the client going away, not a tunnel problem
                    var level = error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled
                        or ForwarderError.ResponseBodyCanceled or ForwarderError.UpgradeRequestCanceled or ForwarderError.UpgradeResponseCanceled
                        ? LogLevel.Debug : LogLevel.Warning;
                    logger.Log(level, "{OriginId} {RequestId} forwarding failed: {Error} {Message}", originId, context.TraceIdentifier, error, exception?.Message);
                }
            });
        }

        private static async Task AcceptTunnelAsync(HttpContext context, TunnelRegistry registry, TunnelAuthorization authorization, IHostApplicationLifetime lifetime, ILogger logger, int maxConnectionsPerOrigin)
        {
            string? originId = context.Request.Query[Constants.OriginIdParameter];
            if (!context.WebSockets.IsWebSocketRequest || !TunnelRegistry.IsValidOriginId(originId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!authorization.IsAuthorized(context))
            {
                if (authorization.IsConfigured)
                    logger.LogWarning("Rejected tunnel from {RemoteIp}: invalid auth token", context.Connection.RemoteIpAddress);
                else
                    logger.LogWarning("Rejected tunnel from {RemoteIp}: the gateway has no auth token configured", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Tell the origin how many tunnels it may keep open, so it won't exhaust the host's WebSocket budget
            if (maxConnectionsPerOrigin > 0)
                context.Response.Headers[Headers.XWebhopMaxConnections] = maxConnectionsPerOrigin.ToString();

            // Echo only the fixed marker when a browser authenticated through a subprotocol, never the token
            var subProtocol = context.WebSockets.WebSocketRequestedProtocols.Contains(Constants.WebHopSubprotocol)
                ? Constants.WebHopSubprotocol
                : null;
            var webSocket = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
            // Close tunnels as soon as the gateway stops; otherwise shutdown waits for each tunnel
            // request until the host's 30 s timeout, and origins reconnect that much later
            using var closing = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
            await registry.RunTunnelAsync(originId!, new TunnelStream(webSocket), closing.Token);
        }
    }
}
