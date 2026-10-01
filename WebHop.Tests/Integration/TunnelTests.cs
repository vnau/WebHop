using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using WebHop.Core;
using WebHop.Server;
using WebHop.Tests.Infrastructure;

namespace WebHop.Tests.Integration
{
    /// <summary>A real gateway and a real UseWebHop app, talking over loopback.</summary>
    public class TunnelTests
    {
        [Fact]
        public async Task Requests_reach_the_app_through_the_gateway()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            using var response = await gateway.Client.GetAsync("/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pong", await response.Content.ReadAsStringAsync());
            Assert.Equal(app.Server.ServerId, response.Headers.GetValues(Headers.XWebhopConnectionId).Single());
            Assert.Equal(WebHopConnectionState.Online, app.Server.Status.State);
        }

        [Fact]
        public async Task Without_a_connected_app_the_gateway_answers_503()
        {
            await using var gateway = await TestGateway.StartAsync();

            using var response = await gateway.Client.GetAsync("/ping");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task The_app_sees_the_visitors_request_details_not_the_tunnels()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/info");
            // A visitor must not be able to fake these; the gateway overwrites them
            request.Headers.Add(Headers.XForwardedProto, "https");
            request.Headers.Add(Headers.XForwardedFor, "6.6.6.6");
            using var response = await gateway.Client.SendAsync(request);
            var info = await response.Content.ReadFromJsonAsync<InfoResponse>();

            Assert.Equal("http", info!.Scheme);
            Assert.Equal(new Uri(gateway.Url).Authority, info.Host);
            Assert.Equal("127.0.0.1", info.RemoteIp);
            Assert.Equal(response.Headers.GetValues(Headers.XWebhopRequestId).Single(), info.TraceIdentifier);
        }

        [Fact]
        public async Task Multi_value_headers_and_late_headers_pass_through()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            using var cookies = await gateway.Client.GetAsync("/cookies");
            using var onStarting = await gateway.Client.GetAsync("/on-starting");

            Assert.Equal(2, cookies.Headers.GetValues("Set-Cookie").Count());
            Assert.Equal("yes", onStarting.Headers.GetValues("X-On-Starting").Single());
        }

        [Fact]
        public async Task An_app_exception_is_a_500_right_away()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            var stopwatch = Stopwatch.StartNew();
            using var response = await gateway.Client.GetAsync("/throw");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        }

        [Fact]
        public async Task Large_bodies_stream_in_both_directions()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();
            const int size = 20 * 1024 * 1024;

            using var upload = await gateway.Client.PostAsync("/count", new ByteArrayContent(new byte[size]));
            var download = await gateway.Client.GetByteArrayAsync($"/bytes/{size}");

            Assert.Equal(size.ToString(), await upload.Content.ReadAsStringAsync());
            Assert.Equal(size, download.Length);
        }

        [Fact]
        public async Task Requests_run_concurrently_up_to_the_number_of_tunnels()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url, maxConnections: 4);
            await app.WaitForTunnelsAsync();

            var stopwatch = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => gateway.Client.GetStringAsync("/slow/1000")));

            // One at a time would take 4 s
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"took {stopwatch.Elapsed}");
        }

        [Fact]
        public async Task WebSockets_work_through_the_tunnel()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(gateway.Url.Replace("http://", "ws://") + "/echo-ws"), CancellationToken.None);
            await ws.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
            var buffer = new byte[64];
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

            Assert.Equal("hello", Encoding.UTF8.GetString(buffer, 0, result.Count));
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ws.CloseStatus);
        }

        [Fact]
        public async Task A_visitor_disconnecting_aborts_the_request_and_the_tunnel_is_replaced()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url, maxConnections: 2);
            await app.WaitForTunnelsAsync();

            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.Client.GetAsync("/wait-for-abort", cts.Token));

            await app.RequestAborted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await app.WaitForTunnelsAsync();
            Assert.Equal("pong", await gateway.Client.GetStringAsync("/ping"));
        }

        [Fact]
        public async Task The_app_reconnects_after_the_gateway_restarts()
        {
            var gateway = await TestGateway.StartAsync();
            var port = gateway.Port;
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            await gateway.DisposeAsync();
            await Eventually.TrueAsync(() => app.Server.Status.State == WebHopConnectionState.Offline, "app notices the gateway is gone");

            await using var restarted = await TestGateway.StartAsync(port: port);
            await app.WaitForTunnelsAsync();

            Assert.Equal("pong", await restarted.Client.GetStringAsync("/ping"));
            Assert.Equal(WebHopConnectionState.Online, app.Server.Status.State);
        }

        [Fact]
        public async Task The_app_starts_before_the_gateway_and_connects_when_it_comes_up()
        {
            // Reserve a port by starting and stopping a gateway on it
            var probe = await TestGateway.StartAsync();
            var port = probe.Port;
            await probe.DisposeAsync();

            await using var app = await TunnelApp.StartAsync($"http://127.0.0.1:{port}/");
            await Task.Delay(500);
            Assert.NotEqual(WebHopConnectionState.Online, app.Server.Status.State);

            await using var gateway = await TestGateway.StartAsync(port: port);
            await app.WaitForTunnelsAsync();
            Assert.Equal("pong", await gateway.Client.GetStringAsync("/ping"));
        }

        [Fact]
        public async Task Stopping_the_app_is_fast_while_tunnels_are_open()
        {
            // Regression: pipes that were never completed made every shutdown wait for Kestrel's 30 s timeout
            await using var gateway = await TestGateway.StartAsync();
            var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();
            Assert.Equal("pong", await gateway.Client.GetStringAsync("/ping"));

            var stopwatch = Stopwatch.StartNew();
            await app.DisposeAsync();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
            await Eventually.TrueAsync(() => gateway.Registry.ServerCount == 0, "gateway drops the stopped app");
        }

        [Fact]
        public async Task Stopping_the_gateway_is_fast_while_tunnels_are_open()
        {
            // Regression: open tunnel requests made every gateway shutdown wait for the host's 30 s timeout
            var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url);
            await app.WaitForTunnelsAsync();

            var stopwatch = Stopwatch.StartNew();
            await gateway.DisposeAsync();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
            await Eventually.TrueAsync(() => app.Server.Status.OpenTunnels == 0, "app sees its tunnels closed");
        }

        [Fact]
        public async Task A_wrong_auth_token_is_rejected()
        {
            await using var gateway = await TestGateway.StartAsync();
            await using var app = await TunnelApp.StartAsync(gateway.Url, authToken: "wrong-token");

            await Eventually.TrueAsync(() => app.Server.Status.State == WebHopConnectionState.Unauthorized, "app reports unauthorized");
            Assert.Equal(0, gateway.Registry.ServerCount);
            using var response = await gateway.Client.GetAsync("/ping");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task An_app_without_an_auth_token_refuses_to_start()
        {
            await using var gateway = await TestGateway.StartAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TunnelApp.StartAsync(gateway.Url, authToken: null));
            Assert.Contains(Constants.AuthTokenEnvironmentVariable, error.Message);
        }

        private sealed record InfoResponse(string Scheme, string Host, string? RemoteIp, string TraceIdentifier);
    }
}
