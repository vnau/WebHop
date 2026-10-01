using WebHop.Core;

namespace WebHop.Server
{
    public sealed class WebHopServerOptions
    {
        /// <summary>
        /// Auth token the gateway requires to open tunnels. Defaults to the WEBHOP_AUTHTOKEN environment
        /// variable; UseWebHop overrides it with the WebHop:AuthToken setting when that is present.
        /// Sent as a bearer token on every tunnel's WebSocket upgrade, so use a wss:// (https://)
        /// gateway URL outside of development.
        /// </summary>
        public string? AuthToken { get; set; } = Environment.GetEnvironmentVariable(Constants.AuthTokenEnvironmentVariable);

        /// <summary>Identifies this server to the gateway; lowercase letters and digits only.</summary>
        public string ServerId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Number of tunnel streams kept open to the gateway. Each carries one HTTP connection,
        /// so this caps the number of concurrent requests (and upgraded WebSockets).
        /// </summary>
        public int MaxConnections { get; set; } = 4;

        /// <summary>Upper bound for the delay between reconnect attempts while the gateway is unreachable.</summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>WebSocket ping interval of tunnel streams.</summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
    }
}
