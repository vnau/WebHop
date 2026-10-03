using WebHop.Core;

namespace WebHop.Gateway
{
    /// <summary>
    /// Works out how many tunnels one origin may keep open, from the host's WebSocket limit. Azure App
    /// Service caps concurrent WebSockets by tier (Free 5, Shared 35, Basic and up unlimited), reported
    /// in <see cref="Constants.WebsiteSkuEnvironmentVariable"/>; <see cref="Constants.MaxWebSocketsSetting"/>
    /// overrides it for other hosts. Origins are given 60% of the budget, leaving room for visitors'
    /// own WebSockets. The gateway advertises this on the tunnel handshake and origins cap themselves to it.
    /// </summary>
    internal static class WebSocketBudget
    {
        /// <summary>Share of the host's WebSocket budget handed to origins' tunnels.</summary>
        private const double OriginShare = 0.6;

        /// <summary>Per-origin tunnel cap to advertise, or 0 when the host sets no limit (unlimited).</summary>
        public static int PerOriginCap(IConfiguration configuration) =>
            PerOriginCap(configuration[Constants.MaxWebSocketsSetting],
                Environment.GetEnvironmentVariable(Constants.WebsiteSkuEnvironmentVariable));

        public static int PerOriginCap(string? maxWebSocketsSetting, string? websiteSku)
        {
            var budget = Budget(maxWebSocketsSetting, websiteSku);
            return budget > 0 ? Math.Max(1, (int)(budget * OriginShare)) : 0;
        }

        /// <summary>Total concurrent WebSockets the host allows, or 0 for unlimited.</summary>
        public static int Budget(string? maxWebSocketsSetting, string? websiteSku)
        {
            if (int.TryParse(maxWebSocketsSetting, out var configured) && configured > 0)
                return configured;

            return websiteSku?.Trim().ToLowerInvariant() switch
            {
                "free" => 5,
                "shared" => 35,
                // Basic and up have no documented WebSocket cap; neither do non-Azure hosts
                _ => 0,
            };
        }
    }
}
