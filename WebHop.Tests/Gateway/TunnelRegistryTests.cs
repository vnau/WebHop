using Microsoft.Extensions.Logging.Abstractions;
using WebHop.Core;
using WebHop.Gateway;
using WebHop.Tests.Infrastructure;

namespace WebHop.Tests.Gateway
{
    public class TunnelRegistryTests
    {
        private readonly TunnelRegistry registry = new(NullLogger<TunnelRegistry>.Instance);

        [Theory]
        [InlineData("abc123", true)]
        [InlineData("0123456789abcdef0123456789abcdef", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("ABC", false)]
        [InlineData("a-b", false)]
        [InlineData("a.b", false)]
        public void Origin_ids_must_be_valid_host_names(string? id, bool valid)
        {
            Assert.Equal(valid, TunnelRegistry.IsValidOriginId(id));
        }

        [Fact]
        public void No_origin_is_picked_while_none_is_connected()
        {
            Assert.Null(registry.PickOrigin());
            Assert.Equal(0, registry.OriginCount);
        }

        [Fact]
        public async Task A_registered_tunnel_can_be_taken_and_carries_data()
        {
            await using var pair = await WebSocketPair.CreateAsync();
            _ = registry.RunTunnelAsync("abc", new TunnelStream(pair.Server), CancellationToken.None);
            await Eventually.TrueAsync(() => registry.OriginCount == 1, "origin registered");

            Assert.Equal("abc", registry.PickOrigin());
            var stream = await registry.TakeStreamAsync("abc", CancellationToken.None);
            await stream.WriteAsync("ping"u8.ToArray());

            var received = new byte[4];
            await using var client = new TunnelStream(pair.Client);
            await client.ReadExactlyAsync(received);
            Assert.Equal("ping"u8.ToArray(), received);
        }

        [Fact]
        public async Task Taking_a_tunnel_of_an_unknown_origin_fails()
        {
            await Assert.ThrowsAsync<IOException>(() => registry.TakeStreamAsync("nobody", CancellationToken.None).AsTask());
        }

        [Fact]
        public async Task Taking_waits_while_all_tunnels_of_the_origin_are_in_use()
        {
            await using var pair = await WebSocketPair.CreateAsync();
            _ = registry.RunTunnelAsync("abc", new TunnelStream(pair.Server), CancellationToken.None);
            await Eventually.TrueAsync(() => registry.OriginCount == 1, "origin registered");
            await registry.TakeStreamAsync("abc", CancellationToken.None);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.TakeStreamAsync("abc", cts.Token).AsTask());
        }

        [Fact]
        public async Task An_origin_is_removed_when_its_idle_tunnel_closes()
        {
            await using var pair = await WebSocketPair.CreateAsync();
            var running = registry.RunTunnelAsync("abc", new TunnelStream(pair.Server), CancellationToken.None);
            await Eventually.TrueAsync(() => registry.OriginCount == 1, "origin registered");

            await new TunnelStream(pair.Client).DisposeAsync();

            await running.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, registry.OriginCount);
            Assert.Null(registry.PickOrigin());
        }

        [Fact]
        public async Task Origins_take_turns()
        {
            await using var first = await WebSocketPair.CreateAsync();
            await using var second = await WebSocketPair.CreateAsync();
            _ = registry.RunTunnelAsync("one", new TunnelStream(first.Server), CancellationToken.None);
            _ = registry.RunTunnelAsync("two", new TunnelStream(second.Server), CancellationToken.None);
            await Eventually.TrueAsync(() => registry.OriginCount == 2, "both origins registered");

            var picks = Enumerable.Range(0, 4).Select(_ => registry.PickOrigin()).ToList();

            Assert.Equal(2, picks.Distinct().Count());
            Assert.NotEqual(picks[0], picks[1]);
            Assert.Equal(picks[0], picks[2]);
        }
    }
}
