using System.Threading.Channels;
using WebHop.Core;

namespace WebHop.Gateway
{
    /// <summary>
    /// Tracks connected WebHop servers and the idle tunnel streams each of them keeps open.
    /// A server is registered while it has at least one tunnel stream connected.
    /// </summary>
    public sealed class TunnelRegistry(ILogger<TunnelRegistry> logger)
    {
        private readonly object sync = new();
        private readonly Dictionary<string, ServerTunnel> servers = [];
        private string[] serverIds = [];
        private int next;

        public int ServerCount => Volatile.Read(ref serverIds).Length;

        /// <summary>Server ids double as host names in YARP destination URLs.</summary>
        public static bool IsValidServerId(string? id) =>
            id is { Length: > 0 and <= 63 } && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

        /// <summary>Round-robin over connected servers; null when none are connected.</summary>
        public string? PickServer()
        {
            var ids = Volatile.Read(ref serverIds);
            if (ids.Length == 0)
                return null;
            return ids[(int)((uint)Interlocked.Increment(ref next) % (uint)ids.Length)];
        }

        /// <summary>
        /// Offers a freshly accepted tunnel stream to the pool and completes when the stream closes.
        /// </summary>
        public async Task RunTunnelAsync(string serverId, TunnelStream stream, CancellationToken ct)
        {
            ServerTunnel tunnel;
            lock (sync)
            {
                if (!servers.TryGetValue(serverId, out tunnel!))
                {
                    servers[serverId] = tunnel = new ServerTunnel();
                    serverIds = [.. servers.Keys];
                    logger.LogInformation("Server {ServerId} connected ({Count} connected)", serverId, servers.Count);
                }
                tunnel.Connections++;
                logger.LogDebug("Tunnel stream of {ServerId} opened ({Count} open)", serverId, tunnel.Connections);
            }

            try
            {
                var entry = new IdleStream(stream);
                // Nothing arrives on an idle tunnel before a request is sent, so a completed
                // read-ahead on a stream still in the pool means the server closed it
                _ = stream.StartReadAhead().ContinueWith(t =>
                {
                    if (entry.TryMarkDead())
                    {
                        logger.LogDebug("Idle tunnel stream of {ServerId} closed: {Reason}", serverId,
                            t.IsCompletedSuccessfully ? t.Result.MessageType.ToString() : t.Exception?.GetBaseException().Message);
                        stream.Dispose();
                    }
                }, TaskScheduler.Default);
                tunnel.Idle.Writer.TryWrite(entry);

                using (ct.Register(stream.Dispose))
                    await stream.Closed;
            }
            finally
            {
                lock (sync)
                {
                    logger.LogDebug("Tunnel stream of {ServerId} closed ({Count} open)", serverId, tunnel.Connections - 1);
                    if (--tunnel.Connections == 0)
                    {
                        servers.Remove(serverId);
                        serverIds = [.. servers.Keys];
                        tunnel.Idle.Writer.TryComplete();
                        logger.LogInformation("Server {ServerId} disconnected ({Count} connected)", serverId, servers.Count);
                    }
                }
            }
        }

        /// <summary>Takes an idle tunnel stream of the server, waiting for one if all are in use.</summary>
        public async ValueTask<Stream> TakeStreamAsync(string serverId, CancellationToken ct)
        {
            ServerTunnel? tunnel;
            lock (sync)
                servers.TryGetValue(serverId, out tunnel);

            if (tunnel is null)
                throw new IOException($"WebHop server {serverId} is not connected.");

            try
            {
                while (true)
                {
                    var entry = await tunnel.Idle.Reader.ReadAsync(ct);
                    if (entry.TryTake())
                        return entry.Stream;
                }
            }
            catch (ChannelClosedException)
            {
                throw new IOException($"WebHop server {serverId} disconnected.");
            }
        }

        private sealed class ServerTunnel
        {
            public Channel<IdleStream> Idle { get; } = Channel.CreateUnbounded<IdleStream>();
            public int Connections { get; set; }
        }

        private sealed class IdleStream(TunnelStream stream)
        {
            private const int Idle = 0, Taken = 1, Dead = 2;
            private int state = Idle;

            public TunnelStream Stream => stream;
            public bool TryTake() => Interlocked.CompareExchange(ref state, Taken, Idle) == Idle;
            public bool TryMarkDead() => Interlocked.CompareExchange(ref state, Dead, Idle) == Idle;
        }
    }
}
