using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace WebHop.Tests.Infrastructure
{
    /// <summary>Two connected WebSockets over a real loopback TCP connection.</summary>
    internal sealed class WebSocketPair : IAsyncDisposable
    {
        private readonly TcpClient serverTcp;
        private readonly TcpClient clientTcp;

        private WebSocketPair(WebSocket server, WebSocket client, TcpClient serverTcp, TcpClient clientTcp)
        {
            Server = server;
            Client = client;
            this.serverTcp = serverTcp;
            this.clientTcp = clientTcp;
        }

        public WebSocket Server { get; }
        public WebSocket Client { get; }

        public static async Task<WebSocketPair> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var clientTcp = new TcpClient();
                var connecting = clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                var serverTcp = await listener.AcceptTcpClientAsync();
                await connecting;

                return new WebSocketPair(
                    WebSocket.CreateFromStream(serverTcp.GetStream(), isServer: true, subProtocol: null, Timeout.InfiniteTimeSpan),
                    WebSocket.CreateFromStream(clientTcp.GetStream(), isServer: false, subProtocol: null, Timeout.InfiniteTimeSpan),
                    serverTcp,
                    clientTcp);
            }
            finally
            {
                listener.Stop();
            }
        }

        public ValueTask DisposeAsync()
        {
            Server.Dispose();
            Client.Dispose();
            serverTcp.Dispose();
            clientTcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
