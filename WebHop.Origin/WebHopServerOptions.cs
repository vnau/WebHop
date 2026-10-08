using WebHop.Core;

namespace WebHop.Origin
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

        /// <summary>Identifies this origin to the gateway; lowercase letters and digits only.</summary>
        public string OriginId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Upper bound on the tunnel streams kept open to the gateway. Each carries one HTTP connection,
        /// so this caps the number of concurrent requests (and upgraded WebSockets). The gateway may
        /// advertise a lower per-origin limit (from its WebSocket budget), and the server honors the
        /// smaller of the two — so this is a ceiling, not a guarantee.
        /// </summary>
        public int MaxConnections { get; set; } = 10;

        /// <summary>Upper bound for the delay between reconnect attempts while the gateway is unreachable.</summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>WebSocket ping interval of tunnel streams.</summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
    }
}
