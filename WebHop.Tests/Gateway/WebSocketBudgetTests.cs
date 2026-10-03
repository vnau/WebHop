using WebHop.Gateway;

namespace WebHop.Tests.Gateway
{
    public class WebSocketBudgetTests
    {
        [Theory]
        // Azure tiers (60% of the host's WebSocket limit, leaving room for visitors)
        [InlineData(null, "Free", 3)]     // 5  -> 3
        [InlineData(null, "free", 3)]     // case-insensitive
        [InlineData(null, "Shared", 21)]  // 35 -> 21
        [InlineData(null, "Basic", 0)]    // unlimited
        [InlineData(null, "Standard", 0)]
        [InlineData(null, null, 0)]       // non-Azure
        // Explicit override wins and is also taken at 60%
        [InlineData("10", "Free", 6)]
        [InlineData("100", null, 60)]
        // A non-positive or unparsable override falls back to the SKU
        [InlineData("0", "Free", 3)]
        [InlineData("nonsense", "Free", 3)]
        public void Per_origin_cap_follows_the_budget(string? maxWebSockets, string? sku, int expected)
        {
            Assert.Equal(expected, WebSocketBudget.PerOriginCap(maxWebSockets, sku));
        }
    }
}
