<h1 align="center">
  <img src="./assets/hopper.svg" alt="Hopper, the WebHop rabbit, peeking out of its burrow" width="160">
  <br/>
  WebHop
</h1>

<p align="center">
  <a href="https://github.com/elebree/WebHop/actions/workflows/ci.yml"><img src="https://github.com/elebree/WebHop/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://codecov.io/gh/elebree/WebHop"><img src="https://codecov.io/gh/elebree/WebHop/graph/badge.svg" alt="Coverage"></a>
  <a href="https://www.nuget.org/packages/WebHop.Origin"><img src="https://img.shields.io/nuget/v/WebHop.Origin?label=WebHop.Origin" alt="WebHop.Origin on NuGet"></a>
  <a href="https://www.nuget.org/packages/WebHop.Gateway"><img src="https://img.shields.io/nuget/v/WebHop.Gateway?label=WebHop.Gateway" alt="WebHop.Gateway on NuGet"></a>
  <a href="https://www.nuget.org/packages/webhop"><img src="https://img.shields.io/nuget/v/webhop?label=webhop%20tool" alt="webhop dotnet tool on NuGet"></a>
  <a href="https://github.com/elebree/WebHop/releases/latest"><img src="https://img.shields.io/github/v/release/elebree/WebHop?label=binaries" alt="Latest release binaries"></a>
  <img src="https://img.shields.io/badge/.NET-9.0%20%7C%2010.0-512BD4" alt=".NET 9.0 | 10.0">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/elebree/WebHop" alt="MIT license"></a>
</p>

WebHop publishes an ASP.NET Core app that runs behind NAT or a firewall through a public **gateway**, with no inbound ports. The app opens WebSocket tunnels out to the gateway. The gateway forwards public HTTP traffic through them with [YARP](https://github.com/dotnet/yarp), and on the app side the tunnels are served by Kestrel like ordinary connections.

```
browser ──HTTP──▶ WebHop.Gateway ◀──WebSocket tunnels── your app (WebHopServer)
                  (public host)                          (behind NAT, no open ports)
```

Everything HTTP passes through: streaming request and response bodies of any size, WebSockets, server-sent events, multi-value headers such as `Set-Cookie`, and client disconnects (`RequestAborted`).

## Packages

| Package | What it is | Install |
|---|---|---|
| [WebHop.Origin](https://www.nuget.org/packages/WebHop.Origin) | Library that tunnels your ASP.NET Core app to a gateway | `dotnet add package WebHop.Origin` |
| [WebHop.Gateway](https://www.nuget.org/packages/WebHop.Gateway) | The gateway, as middleware for any ASP.NET Core app | `dotnet add package WebHop.Gateway` |
| [webhop](https://www.nuget.org/packages/webhop) | An ngrok-style command line tool that exposes any local server | `dotnet tool install -g webhop`, or a [standalone binary](https://github.com/elebree/WebHop/releases/latest) |
| [WebHop.Core](https://www.nuget.org/packages/WebHop.Core) | Code shared by the others; installed with them | |

All packages target .NET 9 and .NET 10. Each [release](https://github.com/elebree/WebHop/releases) also carries Native AOT `webhop` binaries for Windows, Linux and macOS that run without .NET installed.

In this repository, `WebHop.Gateway.Host` is the deployable gateway, `WebHop.CLI` builds `webhop`, and `WebHop.Tests` holds the unit and end-to-end tests (`dotnet test WebHop.Tests`). See [BUILD.md](BUILD.md) for building and releasing.

## Quick start

### 1. Run a gateway

Pick an auth token (any long random string) and run a gateway with it. Origins need the same token to connect; see [Protecting the gateway](#protecting-the-gateway).

```sh
export WEBHOP_AUTHTOKEN=<token>      # PowerShell: $env:WEBHOP_AUTHTOKEN = "<token>"
```

The quickest way is from a clone of this repository:

```sh
dotnet run --project WebHop.Gateway.Host --launch-profile https
```

Or host it in your own ASP.NET Core project with the `WebHop.Gateway` package. `GatewayApp` is the complete standalone gateway:

```csharp
// dotnet new web -n MyGateway && dotnet add package WebHop.Gateway
using WebHop.Gateway;

GatewayApp.Create(args).Run();
```

To add the gateway to an existing app instead, register its middleware. Requests are forwarded to connected origins, and while none is connected they fall through to the rest of your pipeline:

```csharp
builder.Services.AddWebHopGateway();
var app = builder.Build();
app.UseWebSockets();
app.UseWebHopGateway();
app.MapGet("/", () => "No origin connected");   // your own content
```

You can also run it in [Docker](#docker) or on [Azure App Service](#azure-app-service).

### 2. Connect your app

Add the package to your app and serve it through WebHop instead of Kestrel:

```sh
dotnet add package WebHop.Origin
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseWebHop();
```

Start the app with the **gateway's** URL and the same `WEBHOP_AUTHTOKEN` in its environment. With WebHop, `--urls` (or `applicationUrl` in `launchSettings.json`) is where to connect, not where to listen:

```sh
dotnet run --project MyApp --urls https://localhost:7188/
```

Then open https://localhost:7188/ to see the app through the gateway. (7188 is the repository gateway's https port; use your own gateway's URL otherwise.)

You can also fix the gateway URL and options in code:

```csharp
builder.WebHost.UseWebHop("https://gateway.example.com/", options => options.MaxConnections = 32);
```

## Command line tool

`webhop` exposes any local HTTP server through your gateway, much like `ngrok http`. The local server needs no WebHop code, and it doesn't have to be .NET.

Install it as a .NET tool (needs .NET 9 or later):

```sh
dotnet tool install -g webhop
dotnet tool update -g webhop      # later, to upgrade
```

Or download the standalone binary for your platform from the [latest release](https://github.com/elebree/WebHop/releases/latest) (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`). It's a single Native AOT executable that runs without .NET; unpack it somewhere on your `PATH`. On macOS, a binary downloaded with a browser is quarantined; clear that with `xattr -d com.apple.quarantine webhop`.

Then point it at your gateway and forward a local port:

```sh
webhop config add-gateway-url https://my-gateway.azurewebsites.net/
webhop config add-authtoken <token>
webhop http 5000
```

```
WebHop                                                (Ctrl+C to quit)

Session Status                online
Version                       2.0.0
Origin Id                     5c11642ef14844fb98e6cfcb5ac6ea99
Tunnels                       16/16 open
Forwarding                    https://my-gateway.azurewebsites.net -> http://localhost:5000

Connections                   ttl     opn     p50       p90
                              6       0       17 ms     46 ms

HTTP Requests
-------------
01:51:47  GET     /slow                                     200 OK                        1,5 s
01:51:45  GET     /missing?x=1                              404 Not Found                 17 ms
```

The commands and flags follow ngrok's:

| Command | What it does |
|---|---|
| `webhop http 5000` | Forward to `http://localhost:5000`; also accepts `localhost:5000` or a full URL such as `https://localhost:7001` |
| `webhop config add-gateway-url <url>` | Save the gateway to connect to |
| `webhop config add-authtoken <token>` | Save the gateway's auth token |
| `webhop config check` / `config edit` | Show or edit the saved settings |
| `webhop help [command]`, `webhop <command> --help` | Usage and all flags |

| Flag for `webhop http` | Meaning |
|---|---|
| `--url` | Gateway URL, which is also the public URL. Default: `WEBHOP_URL`, then the saved gateway address |
| `--authtoken` | The gateway's auth token. Default: `WEBHOP_AUTHTOKEN`, then the saved auth token |
| `--host-header=rewrite` | Send `Host: localhost:<port>`, for dev servers that reject other hosts such as Vite and the webpack dev server |
| `--log=stdout\|stderr\|<file>` | `stdout`/`stderr` print log lines instead of the live screen (also used when output is redirected); a file path keeps the screen and writes the log to the file |
| `--log-level`, `--log-format=term\|json` | How much to log and in which format |
| `--config` | Use another config file |
| `--connections` | Tunnels to keep open (WebHop-specific) |

Flags can come in any order, as `--flag value` or `--flag=value`.

The local server sees the public Host header and `X-Forwarded-For/Proto/Host`. Self-signed certificates are accepted for local https targets. If the local server isn't running, visitors get a 502 page that says so. `webhop` opens no listening port: it only connects out to the gateway and to your local server.

To run it from a clone of the repository, use `dotnet run --project WebHop.CLI -- http 5000`.

## Protecting the gateway

Only origins that present the gateway's auth token can open tunnels. Set the same auth token on both sides. The environment variable `WEBHOP_AUTHTOKEN` works everywhere:

| Where | Setting |
|---|---|
| Gateway | `WEBHOP_AUTHTOKEN`, or the configuration key `WebHop:AuthToken` (appsettings, user secrets, `WebHop__AuthToken`) |
| Your app | `WEBHOP_AUTHTOKEN`, the configuration key `WebHop:AuthToken`, or `options.AuthToken` in `UseWebHop` |
| `webhop` CLI | `WEBHOP_AUTHTOKEN`, `--authtoken`, or `webhop config add-authtoken <token>` |

If both are set, the `WebHop:AuthToken` setting wins over `WEBHOP_AUTHTOKEN`.

There is no open mode. A gateway without an auth token still starts and serves `/webhop/status`, but it rejects every tunnel and logs an error until one is set. An app or `webhop` without an auth token refuses to start.

The auth token is sent as a bearer token each time a tunnel opens, so use an `https://` gateway URL outside local development. It only controls who can register as an origin; public visitors don't need it.

## Docker

The compose file runs the gateway:

```sh
WEBHOP_AUTHTOKEN=<long random string> docker compose up --build
```

The site is then at http://localhost:8080. You can put `WEBHOP_AUTHTOKEN` in a `.env` file next to `docker-compose.yml` instead; that file is git-ignored. To build one image on its own, build from the repository root:

```sh
docker build -f WebHop.Gateway.Host/Dockerfile -t webhop-gateway .
```

If the gateway runs behind a TLS-terminating reverse proxy (nginx, Traefik, Caddy), set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the gateway so apps see the client's real IP and scheme. The proxy must also pass WebSocket upgrades on `/webhop`; nginx needs `proxy_http_version 1.1` and the `Upgrade`/`Connection` headers for this.

## Azure App Service

- Enable **WebSockets** in the gateway's configuration.
- Set `WEBHOP_AUTHTOKEN` as an application setting.
- Point your app at `https://<your-app>.azurewebsites.net/`.
- Mind the plan's WebSocket limit per instance: Free allows 5, Shared 35, Basic 350, Standard and up have no fixed limit. Every tunnel is one WebSocket, and so is every WebSocket your visitors open. On the capped tiers (Free, Shared) the gateway advertises a per-origin tunnel limit on the handshake and your app caps itself to it automatically, leaving room for visitors — so no manual `MaxConnections` tuning is needed.

## Origin options

`UseWebHop` reads these from the `WebHop` configuration section (appsettings.json, environment variables such as `WebHop__MaxConnections`, and so on). Values set in the `UseWebHop` callback take precedence.

```json
{
  "WebHop": {
    "MaxConnections": 32
  }
}
```

| Option | Default | Meaning |
|---|---|---|
| `AuthToken` | `WEBHOP_AUTHTOKEN` env var | The gateway's auth token (keep it out of appsettings.json in source control) |
| `MaxConnections` | 10 | Tunnels kept open; caps concurrent requests and WebSockets (the gateway may advertise a lower per-origin limit, and the smaller wins) |
| `OriginId` | random | How the gateway identifies this app |
| `MaxReconnectDelay` | 30 s | Upper bound of the reconnect backoff |
| `KeepAliveInterval` | 15 s | WebSocket ping interval |

The app reconnects on its own when the gateway restarts. While no app is connected, the gateway answers 503. A request that gets no response within 120 s gets 504.

## Gateway endpoints

| Path | Purpose |
|---|---|
| `/webhop` | Where apps open their tunnels (WebSocket) |
| `/webhop/status` | Health check |
| anything else | Forwarded to a connected app; with several apps connected, they take turns |

Requests forwarded to the app carry `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`, and `WebHopServer` applies these automatically. Requests and responses also carry `X-Webhop-Origin-Id` (which app served it) and `X-Webhop-Request-Id`.

## Limitations

- A gateway keeps its tunnels in memory, so run a single gateway instance. Behind a load balancer with several instances, a request can land on an instance your app isn't connected to and get a 503.
- There is no WebSocket fallback: networks that block outgoing WebSockets can't open tunnels.
- When you run your app from Visual Studio in Development, pages opened through the gateway may trigger a browser prompt about apps on your device. That is Visual Studio's Browser Link and Hot Reload script connecting back to `localhost`; it doesn't happen outside Development.

## License

[MIT](LICENSE)
