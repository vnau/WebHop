using Microsoft.Extensions.Configuration;
using WebHop.Core;
using WebHop.Gateway;
using WebHop.Server;
using WebHop.Tests.Infrastructure;

namespace WebHop.Tests.Gateway
{
    [Collection(EnvironmentVariablesCollection.Name)]
    public class TunnelAuthorizationTests
    {
        [Fact]
        public void Without_an_auth_token_no_tunnel_is_accepted()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, null);
            var authorization = Create(setting: null);

            Assert.False(authorization.IsConfigured);
            Assert.False(authorization.IsAuthorized(Request("Bearer anything")));
            Assert.False(authorization.IsAuthorized(Request(null)));
        }

        [Theory]
        [InlineData("Bearer s3cr3t-token", true)]
        [InlineData("bearer s3cr3t-token", true)]
        [InlineData("Bearer  s3cr3t-token ", true)]
        [InlineData("Bearer wrong", false)]
        [InlineData("Bearer s3cr3t-token-longer", false)]
        [InlineData("Basic s3cr3t-token", false)]
        [InlineData("s3cr3t-token", false)]
        [InlineData(null, false)]
        public void Only_the_configured_token_is_accepted(string? header, bool accepted)
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, null);
            var authorization = Create(setting: "s3cr3t-token");

            Assert.True(authorization.IsConfigured);
            Assert.Equal(accepted, authorization.IsAuthorized(Request(header)));
        }

        [Fact]
        public void The_environment_variable_is_used_when_the_setting_is_missing()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, "from-env");
            var authorization = Create(setting: null);

            Assert.True(authorization.IsAuthorized(Request("Bearer from-env")));
        }

        [Fact]
        public void The_setting_wins_over_the_environment_variable()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, "from-env");
            var authorization = Create(setting: "from-setting");

            Assert.True(authorization.IsAuthorized(Request("Bearer from-setting")));
            Assert.False(authorization.IsAuthorized(Request("Bearer from-env")));
        }

        [Fact]
        public void Server_options_read_the_same_environment_variable()
        {
            using var env = new EnvironmentVariableScope().Set(Constants.AuthTokenEnvironmentVariable, "from-env");

            Assert.Equal("from-env", new WebHopServerOptions().AuthToken);
        }

        private static TunnelAuthorization Create(string? setting)
        {
            var values = new Dictionary<string, string?>();
            if (setting is not null)
                values[Constants.AuthTokenSetting] = setting;
            return new TunnelAuthorization(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }

        private static HttpRequest Request(string? authorization)
        {
            var context = new DefaultHttpContext();
            if (authorization is not null)
                context.Request.Headers.Authorization = authorization;
            return context.Request;
        }
    }
}
