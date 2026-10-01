using WebHop.Gateway;

namespace WebHop.Tests.Infrastructure
{
    /// <summary>A real gateway on a loopback port, started in-process.</summary>
    internal sealed class TestGateway : IAsyncDisposable
    {
        public const string AuthToken = "test-auth-token";

        private TestGateway(WebApplication app, string url)
        {
            App = app;
            Url = url;
            Client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
        }

        public WebApplication App { get; }
        public string Url { get; }
        public HttpClient Client { get; }
        public TunnelRegistry Registry => App.Services.GetRequiredService<TunnelRegistry>();

        /// <param name="port">0 picks a free port; pass a previous gateway's port to restart "the same" gateway.</param>
        public static async Task<TestGateway> StartAsync(string? authToken = AuthToken, int port = 0)
        {
            List<string> args = ["--urls", $"http://127.0.0.1:{port}", "--Logging:LogLevel:Default=Warning"];
            if (authToken is not null)
                args.Add($"--WebHop:AuthToken={authToken}");

            var app = GatewayApp.Create([.. args]);
            await app.StartAsync();
            return new TestGateway(app, app.Urls.Single());
        }

        public int Port => new Uri(Url).Port;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
