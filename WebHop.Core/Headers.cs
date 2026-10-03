namespace WebHop.Core
{
    public static class Headers
    {
        public const string XForwardedFor = "X-Forwarded-For";
        public const string XForwardedProto = "X-Forwarded-Proto";
        public const string XForwardedHost = "X-Forwarded-Host";
        public const string XWebhopConnectionId = "X-Webhop-Connection-Id";
        public const string XWebhopRequestId = "X-Webhop-Request-Id";

        /// <summary>Tunnel handshake response header: how many tunnels one origin may keep open.</summary>
        public const string XWebhopMaxConnections = "X-Webhop-Max-Connections";
    }
}
