using System.Net;
using WebHop.Core;
using WebHop.Server;
using WebHop.Tests.Infrastructure;

namespace WebHop.Tests.Integration
{
    /// <summary>The gateway falls back to WEBHOP_AUTHTOKEN, so these tests clear it.</summary>
    [Collection(EnvironmentVariablesCollection.Name)]
    public class GatewayWithoutAuthTokenTests
    {
        [Fact]
        public async Task A_gateway_without_an_auth_token_starts_but_accepts_no_tunnels()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, null);
            await using var gateway = await TestGateway.StartAsync(authToken: null);
            await using var app = await TunnelApp.StartAsync(gateway.Url, authToken: "any-token");

            Assert.Equal("WebHop Gateway status", await gateway.Client.GetStringAsync("/webhop/status"));
            await Eventually.TrueAsync(() => app.Server.Status.State == WebHopConnectionState.Unauthorized, "app reports unauthorized");
            using var response = await gateway.Client.GetAsync("/ping");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task A_gateway_can_take_the_auth_token_from_the_environment()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, "env-token");
            await using var gateway = await TestGateway.StartAsync(authToken: null);
            await using var app = await TunnelApp.StartAsync(gateway.Url, authToken: "env-token");

            await app.WaitForTunnelsAsync();
            Assert.Equal("pong", await gateway.Client.GetStringAsync("/ping"));
        }
    }
}
