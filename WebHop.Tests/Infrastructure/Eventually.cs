namespace WebHop.Tests.Infrastructure
{
    internal static class Eventually
    {
        /// <summary>Polls until the condition holds; fails the test with <paramref name="what"/> after the timeout.</summary>
        public static async Task TrueAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail($"Timed out waiting for: {what}");
                await Task.Delay(25);
            }
        }
    }
}
