using System.Net.WebSockets;

namespace WebHop.Core
{
    /// <summary>
    /// Duplex byte stream over a WebSocket. One tunnel stream carries one HTTP connection;
    /// WebSocket message boundaries carry no meaning.
    /// </summary>
    public sealed class TunnelStream(WebSocket webSocket) : Stream
    {
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly byte[] buffer = new byte[16 * 1024];
        // Cancelling a WebSocket receive aborts the socket, so receives are never cancelled:
        // a cancelled read leaves the receive pending and the next read picks it up
        private Task<ValueWebSocketReceiveResult>? pendingReceive;
        private int offset;
        private int count;
        private int closing;

        /// <summary>Completes once the stream has been closed or disposed.</summary>
        public Task Closed => closed.Task;

        /// <summary>
        /// Starts receiving in the background so that a remote close is noticed while the stream sits idle.
        /// The received data is handed to the first read.
        /// </summary>
        public Task<ValueWebSocketReceiveResult> StartReadAhead() => pendingReceive ??= ReceiveCoreAsync();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct = default)
        {
            try
            {
                while (true)
                {
                    if (count > 0)
                    {
                        var n = Math.Min(count, destination.Length);
                        buffer.AsSpan(offset, n).CopyTo(destination.Span);
                        offset += n;
                        count -= n;
                        return n;
                    }

                    if (pendingReceive is null && webSocket.State is not (WebSocketState.Open or WebSocketState.CloseSent))
                        return 0;

                    pendingReceive ??= ReceiveCoreAsync();
                    var result = await pendingReceive.WaitAsync(ct);
                    pendingReceive = null;

                    if (result.MessageType == WebSocketMessageType.Close)
                        return await AcknowledgeCloseAsync();

                    offset = 0;
                    count = result.Count;
                    // A zero-byte read only waits for data to become available
                    if (destination.IsEmpty && count > 0)
                        return 0;
                }
            }
            catch (WebSocketException ex)
            {
                throw new IOException(ex.Message, ex);
            }
        }

        private Task<ValueWebSocketReceiveResult> ReceiveCoreAsync() =>
            webSocket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct = default)
        {
            try
            {
                await webSocket.SendAsync(source, WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
            catch (WebSocketException ex)
            {
                throw new IOException(ex.Message, ex);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Tears the connection down without a close handshake.</summary>
        public void Abort()
        {
            webSocket.Abort();
            _ = CloseAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _ = CloseAsync();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await CloseAsync();
            GC.SuppressFinalize(this);
        }

        private async ValueTask<int> AcknowledgeCloseAsync()
        {
            await CloseAsync();
            return 0;
        }

        private async Task CloseAsync()
        {
            if (Interlocked.Exchange(ref closing, 1) != 0)
            {
                await closed.Task;
                return;
            }

            try
            {
                if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
                }
            }
            catch
            {
                // The peer is already gone; nothing left to shut down gracefully
            }
            finally
            {
                webSocket.Dispose();
                // Disposing fails a receive nobody waits for any more; observe it
                _ = pendingReceive?.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                closed.TrySetResult();
            }
        }
    }
}
