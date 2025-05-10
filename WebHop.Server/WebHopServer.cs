using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebHop.Core;
using WebHop.Core.Abstract;
using HttpResponseMessage = WebHop.Core.Models.HttpResponseMessage;

namespace WebHop.Server
{
    public sealed class WebHopServer : IServer, IMessageHandler<Core.Models.HttpRequestMessage>, IAsyncDisposable
    {
        private HubConnection? hubConnection;
        private IApplicationProcessor? applicationProcessor;

        private FeatureCollection CreateBaseFeatures()
        {
            var features = new FeatureCollection();
            var serverAddressesFeature = new ServerAddressesFeature();
            features.Set<IServerAddressesFeature>(serverAddressesFeature);
            return features;
        }

        public WebHopServer()
        {
            Features = CreateBaseFeatures();
        }

        public async Task ProcessMessageAsync(Core.Models.HttpRequestMessage request)
        {
            Console.WriteLine($"[WebHopServer] Received request: {request.Method} {request.Path.ToString()}");

            if (request == null || applicationProcessor == null)
                return;

            var features = CreateBaseFeatures();
            var response = await applicationProcessor.ProcessRequestAsync(features, request, null, CancellationToken.None);

            await hubConnection.InvokeAsync(nameof(IMessageHandler<HttpResponseMessage>.ProcessMessageAsync), response);
        }

        public IFeatureCollection Features { get; private set; } = new FeatureCollection();

        public async ValueTask DisposeAsync()
        {
            await hubConnection.StopAsync();
            await hubConnection.DisposeAsync();
        }

        public void Dispose()
        {
            hubConnection.StopAsync().GetAwaiter().GetResult();
            hubConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async Task StartAsync<TContext>(
            IHttpApplication<TContext> application, CancellationToken ct) where TContext : notnull
        {
            applicationProcessor = new WebHopRequestProcessor<TContext>(application);
            var addressesFeature = Features.Get<IServerAddressesFeature>();
            var configuredUrl = new Uri(addressesFeature?.Addresses?.FirstOrDefault());
            var hubUrl = configuredUrl.AbsolutePath == "/" ? new Uri(configuredUrl, Constants.DefaultWebHopEndpoint) : configuredUrl;
            hubConnection = new HubConnectionBuilder()
              .WithUrl(hubUrl, options => { })
              .ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information))
              .WithAutomaticReconnect()
              .AddMessagePackProtocol()
              .Build();

            hubConnection.Closed += async (error) =>
            {
                Console.WriteLine($"Connection closed: {error?.Message}");
            };

            hubConnection.Reconnected += (connectionId) =>
            {
                Console.WriteLine($"Reconnected: {connectionId}");
                return Task.CompletedTask;
            };

            hubConnection.On(
                nameof(IMessageHandler<Core.Models.HttpRequestMessage>.ProcessMessageAsync),
                (Func<Core.Models.HttpRequestMessage, Task>)ProcessMessageAsync
            );

            await hubConnection.StartAsync(ct);
        }

        public Task StopAsync(CancellationToken ct)
        {
            return hubConnection?.StopAsync(ct) ?? Task.CompletedTask;
        }
    }
}
