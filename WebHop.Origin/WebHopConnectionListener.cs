using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using WebHop.Core;

namespace WebHop.Origin
{
    /// <summary>Kestrel endpoint that is reached through a WebHop gateway.</summary>
    internal sealed class WebHopEndPoint(Uri tunnelUri) : EndPoint
    {
        public Uri TunnelUri => tunnelUri;
        public override string ToString() => tunnelUri.ToString();
    }

    internal sealed class WebHopConnectionListenerFactory(WebHopServerOptions options, ILoggerFactory loggerFactory) : IConnectionListenerFactory
    {
        public WebHopConnectionListener? Listener { get; private set; }

        public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken ct)
        {
            if (endpoint is not WebHopEndPoint webHopEndPoint)
                throw new NotSupportedException($"{endpoint.GetType()} is not supported by the WebHop transport.");

            Listener = new WebHopConnectionListener(webHopEndPoint, options, loggerFactory.CreateLogger<WebHopServer>());
            return ValueTask.FromResult<IConnectionListener>(Listener);
        }
    }

    /// <summary>
    /// Keeps up to <see cref="WebHopServerOptions.MaxConnections"/> tunnel streams open to the gateway
    /// and hands each one to Kestrel as an accepted connection.
    /// </summary>
    internal sealed class WebHopConnectionListener(WebHopEndPoint endpoint, WebHopServerOptions options, ILogger logger) : IConnectionListener
    {
        private enum Failure { None, Unreachable, Unauthorized, Refused }

        // Pulsed when a tunnel closes, so a parked AcceptAsync can open a replacement.
        private readonly SemaphoreSlim room = new(0);
        private readonly CancellationTokenSource unbind = new();
        private readonly string gateway = endpoint.TunnelUri.GetLeftPart(UriPartial.Path);
        private int open;
        // Per-origin tunnel cap the gateway advertised on the handshake; 0 until learned (then unlimited stays 0).
        private volatile int advertisedCap;

        /// <summary>The smaller of the configured ceiling and any cap the gateway advertised.</summary>
        private int EffectiveMax => advertisedCap > 0 ? Math.Min(options.MaxConnections, advertisedCap) : options.MaxConnections;

        // Kestrel calls AcceptAsync one at a time, so these are only written there;
        // Status reads the volatile ones from other threads.
        // Each kind of failure is logged once; repeats go to Debug until the state changes.
        private volatile bool connected;
        private volatile Failure lastFailure;
        private volatile string? lastError;
        private bool refusalLogged;

        public EndPoint EndPoint => endpoint;

        public WebHopOriginStatus Status
        {
            get
            {
                var count = Volatile.Read(ref open);
                var state = count > 0 ? WebHopConnectionState.Online
                    : lastFailure switch
                    {
                        Failure.Unauthorized => WebHopConnectionState.Unauthorized,
                        Failure.Unreachable => WebHopConnectionState.Offline,
                        // All tunnels dropped after being online: reconnecting, even before an attempt failed
                        _ when connected => WebHopConnectionState.Offline,
                        _ => WebHopConnectionState.Connecting,
                    };
                return new WebHopOriginStatus(state, count, EffectiveMax, lastError);
            }
        }

        public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, unbind.Token);
            try
            {
                // Keep EffectiveMax tunnels open; a closed tunnel frees a slot. Kestrel calls AcceptAsync
                // one at a time, so this gate needs no extra locking.
                while (Volatile.Read(ref open) >= EffectiveMax)
                    await room.WaitAsync(cts.Token);

                var delay = TimeSpan.FromSeconds(1);
                while (true)
                {
                    var webSocket = new ClientWebSocket();
                    webSocket.Options.KeepAliveInterval = options.KeepAliveInterval;
                    webSocket.Options.CollectHttpResponseDetails = true;
                    if (!string.IsNullOrEmpty(options.AuthToken))
                        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + options.AuthToken);
                    try
                    {
                        await webSocket.ConnectAsync(endpoint.TunnelUri, cts.Token);
                        LearnAdvertisedCap(webSocket);
                        OnConnected(Interlocked.Increment(ref open));
                        return new WebHopConnection(new TunnelStream(webSocket), endpoint, () =>
                        {
                            Interlocked.Decrement(ref open);
                            room.Release();
                        });
                    }
                    catch (Exception ex) when (!cts.IsCancellationRequested)
                    {
                        var status = webSocket.HttpStatusCode;
                        webSocket.Dispose();
                        delay = OnFailed(status, ex, delay);
                        await Task.Delay(delay, cts.Token);
                        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxReconnectDelay.Ticks));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Kestrel expects null once the listener is unbound
                return null;
            }
        }

        /// <summary>Reads the per-origin tunnel cap the gateway advertised on the handshake, if any.</summary>
        private void LearnAdvertisedCap(ClientWebSocket webSocket)
        {
            if (webSocket.HttpResponseHeaders is not { } headers)
                return;
            foreach (var header in headers)
            {
                if (!string.Equals(header.Key, Headers.XWebhopMaxConnections, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (int.TryParse(header.Value.FirstOrDefault(), out var value) && value > 0)
                    advertisedCap = value;
                return;
            }
        }

        private void OnConnected(int count)
        {
            if (!connected)
            {
                connected = true;
                logger.LogInformation("Connected to WebHop gateway {Gateway} as {OriginId}", gateway, options.OriginId);
            }

            if (count == options.MaxConnections)
            {
                if (refusalLogged)
                    logger.LogInformation("All {Max} tunnels to the WebHop gateway are open now", options.MaxConnections);
                else
                    logger.LogDebug("All {Max} tunnels to the WebHop gateway are open", options.MaxConnections);
                refusalLogged = false;
            }
            else
            {
                logger.LogDebug("Tunnel {Count}/{Max} to the WebHop gateway open", count, options.MaxConnections);
            }

            // A refusal stays relevant until all tunnels are open
            if (!refusalLogged)
                lastError = null;
            lastFailure = Failure.None;
        }

        /// <summary>Logs the failure and returns the delay before the next attempt.</summary>
        private TimeSpan OnFailed(HttpStatusCode status, Exception ex, TimeSpan delay)
        {
            var current = Volatile.Read(ref open);
            var reason = status != 0 ? $"HTTP {(int)status}" : ex.Message;
            // Refused only when the gateway's host answered (e.g. Azure's 503 beyond its WebSocket limit);
            // failing to connect at all is an outage, even while the last tunnels are still closing
            var failure = status == HttpStatusCode.Unauthorized ? Failure.Unauthorized
                : status != 0 && current > 0 ? Failure.Refused
                : Failure.Unreachable;

            lastError = failure switch
            {
                Failure.Unauthorized => "the gateway rejected the auth token",
                Failure.Refused => $"the gateway refused tunnel {current + 1}/{options.MaxConnections} ({reason}); the host probably limits concurrent WebSockets",
                _ => reason,
            };

            switch (failure)
            {
                case Failure.Unauthorized:
                    if (lastFailure != Failure.Unauthorized)
                        logger.LogError("WebHop gateway {Gateway} rejected the auth token ({Variable} / {Setting}). Retrying with backoff up to {Max}s",
                            gateway, Constants.AuthTokenEnvironmentVariable, Constants.AuthTokenSetting, options.MaxReconnectDelay.TotalSeconds);
                    else
                        logger.LogDebug("WebHop gateway still rejects the auth token. Retrying in {Delay}s", delay.TotalSeconds);
                    break;

                case Failure.Refused:
                    // The gateway works, since other tunnels are open, but it won't take more. Usually the host
                    // limits concurrent WebSockets. Retrying fast would only produce more refusals.
                    delay = options.MaxReconnectDelay;
                    if (!refusalLogged)
                    {
                        refusalLogged = true;
                        logger.LogWarning(
                            "WebHop gateway {Gateway} refused tunnel {Next}/{Max} ({Reason}) while {Open} are open. " +
                            "The host probably limits concurrent WebSockets (Azure App Service Free plan: 5 per instance). " +
                            "Serving with {Open} tunnels and retrying every {Delay}s; set WebHop:MaxConnections to {Suggested} " +
                            "to stop retrying and leave room for visitors' own WebSockets",
                            gateway, current + 1, options.MaxConnections, reason, current, current, delay.TotalSeconds, Math.Max(1, current - 1));
                    }
                    else
                    {
                        logger.LogDebug("WebHop gateway refused tunnel {Next}/{Max} ({Reason}). Retrying in {Delay}s", current + 1, options.MaxConnections, reason, delay.TotalSeconds);
                    }
                    break;

                default:
                    if (lastFailure != Failure.Unreachable)
                        logger.LogWarning("Cannot reach WebHop gateway {Gateway}: {Reason}. Retrying with backoff up to {Max}s",
                            gateway, reason, options.MaxReconnectDelay.TotalSeconds);
                    else
                        logger.LogDebug("WebHop gateway still unreachable ({Reason}). Retrying in {Delay}s", reason, delay.TotalSeconds);
                    connected = false;
                    refusalLogged = false;
                    break;
            }

            lastFailure = failure;
            return delay;
        }

        public ValueTask UnbindAsync(CancellationToken ct)
        {
            unbind.Cancel();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            unbind.Cancel();
            unbind.Dispose();
            room.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A tunnel stream as a Kestrel connection. Like Kestrel's socket transport, it receives in the
    /// background so that Kestrel notices a closed tunnel (and fires RequestAborted) while a request runs.
    /// </summary>
    internal sealed class WebHopConnection : ConnectionContext
    {
        private readonly TunnelStream stream;
        private readonly Action onClosed;
        private readonly Pipe input = new();
        private readonly Pipe output = new();
        private readonly CancellationTokenSource connectionClosed = new();
        private readonly Task receiving;
        private readonly Task sending;
        private int disposed;

        public WebHopConnection(TunnelStream stream, EndPoint endpoint, Action onClosed)
        {
            this.stream = stream;
            this.onClosed = onClosed;
            Transport = new DuplexPipe(input.Reader, output.Writer);
            LocalEndPoint = endpoint;
            receiving = ReceiveAsync();
            sending = SendAsync();
        }

        public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
        public override IDuplexPipe Transport { get; set; }

        /// <summary>Kestrel aborts the running request (RequestAborted) when this fires.</summary>
        public override CancellationToken ConnectionClosed
        {
            get => connectionClosed.Token;
            set => throw new NotSupportedException();
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            stream.Abort();
            output.Reader.CancelPendingRead();
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            // Like Kestrel's socket transport, complete the pipe ends Kestrel used; otherwise the send
            // loop waits for more output until Kestrel's shutdown timeout (30s) aborts the connection
            await output.Writer.CompleteAsync();
            await input.Reader.CompleteAsync();

            await sending;
            await stream.DisposeAsync();
            await receiving;
            onClosed();
        }

        private async Task ReceiveAsync()
        {
            Exception? error = null;
            var remoteClosed = true;
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(input.Writer.GetMemory(4096));
                    if (read == 0)
                        break;

                    input.Writer.Advance(read);
                    var flush = await input.Writer.FlushAsync();
                    if (flush.IsCompleted || flush.IsCanceled)
                    {
                        // Kestrel stopped reading; the tunnel itself is still fine
                        remoteClosed = false;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                error = new ConnectionResetException(ex.Message, ex);
            }

            await input.Writer.CompleteAsync(error);

            if (remoteClosed)
                ThreadPool.UnsafeQueueUserWorkItem(static cts => cts.Cancel(), connectionClosed, preferLocal: false);
        }

        private async Task SendAsync()
        {
            try
            {
                while (true)
                {
                    var result = await output.Reader.ReadAsync();
                    if (result.IsCanceled)
                        break;

                    foreach (var segment in result.Buffer)
                        await stream.WriteAsync(segment);
                    output.Reader.AdvanceTo(result.Buffer.End);

                    if (result.IsCompleted)
                        break;
                }
            }
            catch
            {
                // The tunnel is gone; Kestrel learns about it from the receive side
            }
            finally
            {
                await output.Reader.CompleteAsync();
                // Kestrel is done writing: close the tunnel so the gateway sees end of stream
                await stream.DisposeAsync();
            }
        }

        private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
        {
            public PipeReader Input => input;
            public PipeWriter Output => output;
        }
    }
}
