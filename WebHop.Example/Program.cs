var builder = WebApplication.CreateBuilder(args);

string serverId = Guid.NewGuid().ToString().Split("-").First();

builder.WebHost.UseWebHop();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/ping", () =>
{
    return $"Hello from Server {serverId}!";
});

app.Run();