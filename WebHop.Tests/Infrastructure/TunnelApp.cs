using System.Net.WebSockets;
using Microsoft.AspNetCore.Hosting.Server;
using WebHop.Server;

namespace WebHop.Tests.Infrastructure
{
    /// <summary>An ASP.NET Core app served through a gateway with UseWebHop, with endpoints for the tests.</summary>
    internal sealed class TunnelApp : IAsyncDisposable
    {
        private TunnelApp(WebApplication app)
        {
            App = app;
            Server = (WebHopServer)app.Services.GetRequiredService<IServer>();
        }

        public WebApplication App { get; }
        public WebHopServer Server { get; }

        /// <summary>Completes when a request to /wait-for-abort sees RequestAborted fire.</summary>
        public TaskCompletionSource RequestAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task<TunnelApp> StartAsync(string gatewayUrl, string? authToken = TestGateway.AuthToken, int maxConnections = 4) =>
            StartAsync(gatewayUrl, o =>
            {
                o.AuthToken = authToken;
                o.MaxConnections = maxConnections;
            });

        public static async Task<TunnelApp> StartAsync(string gatewayUrl, Action<WebHopServerOptions> configure)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.UseWebHop(gatewayUrl, o =>
            {
                o.MaxReconnectDelay = TimeSpan.FromSeconds(1);
                configure(o);
            });

            var app = builder.Build();
            var tunnelApp = new TunnelApp(app);
            MapEndpoints(app, tunnelApp);
            await app.StartAsync();
            return tunnelApp;
        }

        public Task WaitForTunnelsAsync(int? count = null) =>
            Eventually.TrueAsync(() => Server.Status.OpenTunnels >= (count ?? Server.Status.MaxTunnels), "tunnels open");

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }

        private static void MapEndpoints(WebApplication app, TunnelApp tunnelApp)
        {
            app.UseWebSockets();

            app.MapGet("/ping", () => "pong");

            app.MapGet("/info", (HttpContext c) => new
            {
                scheme = c.Request.Scheme,
                host = c.Request.Host.Value,
                remoteIp = c.Connection.RemoteIpAddress?.ToString(),
                traceIdentifier = c.TraceIdentifier,
            });

            app.MapGet("/cookies", (HttpContext c) =>
            {
                c.Response.Cookies.Append("a", "1");
                c.Response.Cookies.Append("b", "2");
                return "ok";
            });

            app.MapGet("/on-starting", (HttpContext c) =>
            {
                c.Response.OnStarting(() =>
                {
                    c.Response.Headers["X-On-Starting"] = "yes";
                    return Task.CompletedTask;
                });
                return "ok";
            });

            app.MapGet("/throw", () => { throw new InvalidOperationException("boom"); });

            app.MapGet("/bytes/{count:int}", (int count) => Results.Bytes(new byte[count], "application/octet-stream"));

            app.MapPost("/count", async (HttpRequest request) =>
            {
                long total = 0;
                var buffer = new byte[81920];
                int read;
                while ((read = await request.Body.ReadAsync(buffer)) > 0)
                    total += read;
                return total;
            });

            app.MapGet("/slow/{ms:int}", async (int ms) =>
            {
                await Task.Delay(ms);
                return "done";
            });

            app.MapGet("/wait-for-abort", async (HttpContext c) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), c.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    tunnelApp.RequestAborted.TrySetResult();
                }
            });

            app.Map("/echo-ws", async (HttpContext c) =>
            {
                using var ws = await c.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[4096];
                while (true)
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }
                    await ws.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                }
            });
        }
    }
}
