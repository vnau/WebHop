using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using WebHop.Core.Abstract;
using WebHop.Gateway.Models;

namespace WebHop.Gateway
{
    /// <summary>
    ///  Hub and shared data 
    /// </summary>
    public class WebHopHub : Hub, IMessageHandler<Core.Models.HttpResponseMessage>
    {
        // For tracking connected servers
        public static ConcurrentDictionary<string, IClientProxy> ActiveServers = new();
        public static ConcurrentDictionary<string, PendingRequest> PendingRequests = new();

        public WebHopHub()
        {
        }

        public override Task OnConnectedAsync()
        {
            ActiveServers[Context.ConnectionId] = Clients.Client(Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            ActiveServers.TryRemove(Context.ConnectionId, out _);
            return base.OnDisconnectedAsync(exception);
        }

        public async Task ProcessMessageAsync(Core.Models.HttpResponseMessage response)
        {
            if (PendingRequests.TryRemove(response.Id, out var httpContext))
            {
                httpContext.Context.Response.StatusCode = response.StatusCode;
                foreach (var header in response.Headers)
                    httpContext.Context.Response.Headers[header.Key] = header.Value;

                if (response.Body.Length > 0)
                    await httpContext.Context.Response.Body.WriteAsync(response.Body);
                httpContext.Semaphore.Release();
            }
            else
            {
                Console.WriteLine($"[WebHopHub] Purge response {response.Id}!");
            }
        }
    }
}