using Microsoft.AspNetCore.Hosting.Server;
using WebHop.Server;

var builder = WebApplication.CreateBuilder(args);

string serverId = Guid.NewGuid().ToString().Split("-").First();

builder.Services.AddSingleton<IServer>(new WebHopServer());

var app = builder.Build();

app.MapGet("/", () =>
{
    return $"Hello from Server {serverId}!";
});

app.Run();