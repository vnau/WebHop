using System.Threading.Channels;
using WebHop.Core;

namespace WebHop.Gateway
{
    /// <summary>
    /// Tracks connected WebHop origins and the idle tunnel streams each of them keeps open.
    /// An origin is registered while it has at least one tunnel stream connected.
    /// </summary>
    public sealed class TunnelRegistry(ILogger<TunnelRegistry> logger)
    {
        private readonly object sync = new();
        private readonly Dictionary<string, OriginTunnel> origins = [];
        private string[] originIds = [];
        private int next;

        public int OriginCount => Volatile.Read(ref originIds).Length;

        /// <summary>Origin ids double as host names in YARP destination URLs.</summary>
        public static bool IsValidOriginId(string? id) =>
            id is { Length: > 0 and <= 63 } && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

        /// <summary>Round-robin over connected origins; null when none are connected.</summary>
        public string? PickOrigin()
        {
            var ids = Volatile.Read(ref originIds);
            if (ids.Length == 0)
                return null;
            return ids[(int)((uint)Interlocked.Increment(ref next) % (uint)ids.Length)];
        }

        /// <summary>
        /// Offers a freshly accepted tunnel stream to the pool and completes when the stream closes.
        /// </summary>
        public async Task RunTunnelAsync(string originId, TunnelStream stream, CancellationToken ct)
        {
            OriginTunnel tunnel;
            lock (sync)
            {
                if (!origins.TryGetValue(originId, out tunnel!))
                {
                    origins[originId] = tunnel = new OriginTunnel();
                    originIds = [.. origins.Keys];
                    logger.LogInformation("Origin {OriginId} connected ({Count} connected)", originId, origins.Count);
                }
                tunnel.Connections++;
                logger.LogDebug("Tunnel stream of {OriginId} opened ({Count} open)", originId, tunnel.Connections);
            }

            try
            {
                var entry = new IdleStream(stream);
                // Nothing arrives on an idle tunnel before a request is sent, so a completed
                // read-ahead on a stream still in the pool means the origin closed it
                _ = stream.StartReadAhead().ContinueWith(t =>
                {
                    if (entry.TryMarkDead())
                    {
                        logger.LogDebug("Idle tunnel stream of {OriginId} closed: {Reason}", originId,
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
                    logger.LogDebug("Tunnel stream of {OriginId} closed ({Count} open)", originId, tunnel.Connections - 1);
                    if (--tunnel.Connections == 0)
                    {
                        origins.Remove(originId);
                        originIds = [.. origins.Keys];
                        tunnel.Idle.Writer.TryComplete();
                        logger.LogInformation("Origin {OriginId} disconnected ({Count} connected)", originId, origins.Count);
                    }
                }
            }
        }

        /// <summary>Takes an idle tunnel stream of the origin, waiting for one if all are in use.</summary>
        public async ValueTask<Stream> TakeStreamAsync(string originId, CancellationToken ct)
        {
            OriginTunnel? tunnel;
            lock (sync)
                origins.TryGetValue(originId, out tunnel);

            if (tunnel is null)
                throw new IOException($"WebHop origin {originId} is not connected.");

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
                throw new IOException($"WebHop origin {originId} disconnected.");
            }
        }

        private sealed class OriginTunnel
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
