using WebHop.Core;
using WebHop.Core.Abstract;
using WebHop.Core.Extensions;
using WebHop.Gateway;
using WebHop.Gateway.Models;
using HttpResponseMessage = WebHop.Core.Models.HttpResponseMessage;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
});

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 100 * 1024 * 1024;
});

var app = builder.Build();

app.UseRouting();

app.MapGet(Constants.DefaultWebHopEndpoint + "/status", () =>
{
    return "WebHop Gateway status";
});

app.MapHub<WebHopHub>(Constants.DefaultWebHopEndpoint, options =>
{
    //options.LongPolling.PollTimeout = TimeSpan.FromSeconds(30);
    //options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents;
});

app.MapFallback("{**path}", async context =>
{
    var ct = context.RequestAborted;
    if (WebHopHub.ActiveServers.IsEmpty)
    {
        context.Response.StatusCode = (int)System.Net.HttpStatusCode.ServiceUnavailable;
        return;
    }

    var index = (int)((DateTime.UtcNow.Ticks / 1000000) % WebHopHub.ActiveServers.Count);
    var connectionId = WebHopHub.ActiveServers.Keys.ElementAt(index);
    var server = WebHopHub.ActiveServers[connectionId];

    var requestId = context.TraceIdentifier;
    var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
    headers[Headers.XForwardedFor] = $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}";
    headers[Headers.XWebhopConnectionId] = connectionId;
    headers[Headers.XWebhopRequestId] = requestId;
    var request = new WebHop.Core.Models.HttpRequestMessage
    {
        Id = requestId,
        Method = context.Request.Method,
        Scheme = context.Request.Scheme,
        Protocol = context.Request.Protocol,
        Path = context.Request.Path,
        QueryString = context.Request.QueryString.ToString(),
        Headers = headers,
        Body = await context.Request.Body.ToArrayAsync(ct)
    };

    var pendingRequest = new PendingRequest
    {
        Semaphore = new SemaphoreSlim(0),
        Context = context
    };

    using (pendingRequest.Semaphore)
    {
        WebHopHub.PendingRequests[request.Id] = pendingRequest;
        Console.WriteLine($"[WebHopHub] -> {connectionId} {requestId} Start request ({WebHopHub.PendingRequests.Count} in queue)");

        pendingRequest.Context.Response.StatusCode = (int)System.Net.HttpStatusCode.GatewayTimeout;

        pendingRequest.Context.Response.Headers[Headers.XWebhopConnectionId] = new Microsoft.Extensions.Primitives.StringValues(connectionId);
        pendingRequest.Context.Response.Headers[Headers.XWebhopRequestId] = new Microsoft.Extensions.Primitives.StringValues(requestId);
        await server.SendCoreAsync(nameof(IMessageHandler<HttpResponseMessage>.ProcessMessageAsync), [request], ct);

        var success = await pendingRequest.Semaphore.WaitAsync(TimeSpan.FromSeconds(120), ct);

        WebHopHub.PendingRequests.TryRemove(request.Id, out _);

        if (!success)
        {
            Console.WriteLine($"[WebHopHub] XX {connectionId} {requestId} FAILED request ({WebHopHub.PendingRequests.Count} left in queue)");
        }
        else
        {
            Console.WriteLine($"[WebHopHub] <- {connectionId} {requestId} Completed request ({WebHopHub.PendingRequests.Count} left in queue)");
        }
    }
});

app.Run();
