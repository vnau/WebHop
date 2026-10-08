namespace WebHop.Core
{
    public static class Constants
    {
        public const string DefaultWebHopEndpoint = "/webhop";
        public const string WebHopProtoVersion = "2.0";

        /// <summary>Query string parameter an origin uses to identify itself when opening a tunnel.</summary>
        public const string OriginIdParameter = "id";

        /// <summary>
        /// WebSocket subprotocol the gateway selects when a tunnel authenticates through the handshake's
        /// <c>Sec-WebSocket-Protocol</c> header instead of <c>Authorization</c> (browsers cannot set the
        /// latter). The token rides as a second offered subprotocol: <c>webhop.token.&lt;base64url&gt;</c>.
        /// The gateway echoes only this fixed marker, never the token.
        /// </summary>
        public const string WebHopSubprotocol = "webhop";

        /// <summary>Prefix of the offered subprotocol that carries a base64url-encoded auth token.</summary>
        public const string TokenSubprotocolPrefix = "webhop.token.";

        /// <summary>Environment variable holding the auth token, read by the gateway, apps and the CLI alike.</summary>
        public const string AuthTokenEnvironmentVariable = "WEBHOP_AUTHTOKEN";

        /// <summary>
        /// Configuration key for the auth token (appsettings, user secrets, Azure app settings), read by the
        /// gateway and apps. Takes precedence over <see cref="AuthTokenEnvironmentVariable"/>.
        /// </summary>
        public const string AuthTokenSetting = "WebHop:AuthToken";

        /// <summary>
        /// Optional gateway setting (<c>WebHop:MaxWebSockets</c>) overriding the host's total WebSocket
        /// budget. When unset the gateway infers it from <see cref="WebsiteSkuEnvironmentVariable"/>.
        /// </summary>
        public const string MaxWebSocketsSetting = "WebHop:MaxWebSockets";

        /// <summary>Azure App Service sets this to the pricing tier (Free, Shared, Basic, ...).</summary>
        public const string WebsiteSkuEnvironmentVariable = "WEBSITE_SKU";
    }
}
