<p align="center"><img src="webhop-animated.svg" alt="Hopper, the WebHop rabbit, peeking out of its burrow" width="160"></p>

# WebHop

WebHop publishes an ASP.NET Core app that runs behind NAT or a firewall through a public **gateway**, with no inbound ports. The app opens WebSocket tunnels out to the gateway. The gateway forwards public HTTP traffic through them with [YARP](https://github.com/dotnet/yarp), and on the app side the tunnels are served by Kestrel like ordinary connections.

```
browser ──HTTP──▶ WebHop.Gateway ◀──WebSocket tunnels── your app (WebHopServer)
                  (public host)                          (behind NAT, no open ports)
```

Everything HTTP passes through: streaming request and response bodies of any size, WebSockets, server-sent events, multi-value headers such as `Set-Cookie`, and client disconnects (`RequestAborted`).

## Projects

| Project | What it is |
|---|---|
| `WebHop.Gateway` | The public gateway |
| `WebHop.Origin` | Library that tunnels your app to a gateway |
| `WebHop.Core` | Code shared by both |
| `WebHop.CLI` | `webhop`, an ngrok-style command line tool that exposes any local server |
| `WebHop.Example` | Sample app published through a gateway |
| `WebHop.Tests` | Unit and end-to-end tests: `dotnet test WebHop.Tests` |

## Quick start

Pick an auth token (any long random string) and run a gateway with it. Origins need the same token to connect; see [Protecting the gateway](#protecting-the-gateway).

```sh
export WEBHOP_AUTHTOKEN=<token>      # PowerShell: $env:WEBHOP_AUTHTOKEN = "<token>"
dotnet run --project WebHop.Gateway.Host --launch-profile https
```

Reference `WebHop.Origin` from your app and serve it through WebHop instead of Kestrel:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseWebHop();
```

Start the app with the **gateway's** URL and the same `WEBHOP_AUTHTOKEN` in its environment. With WebHop, `--urls` (or `applicationUrl` in `launchSettings.json`) is where to connect, not where to listen:

```sh
dotnet run --project WebHop.Example --urls https://localhost:7188/
```

Then open https://localhost:7188/ to see the app through the gateway.

You can also fix the gateway URL and options in code:

```csharp
builder.WebHost.UseWebHop("https://gateway.example.com/", options => options.MaxConnections = 32);
```

## Command line tool

`webhop` exposes any local HTTP server through your gateway, much like `ngrok http`. The local server needs no WebHop code, and it doesn't have to be .NET.

```sh
webhop config add-gateway-url https://my-gateway.azurewebsites.net/
webhop config add-authtoken <token>
webhop http 5000
```

```
WebHop                                                (Ctrl+C to quit)

Session Status                online
Version                       1.0.0
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

Run it from the repository with `dotnet run --project WebHop.CLI -- http 5000`, or install it as a .NET tool:

```sh
dotnet pack WebHop.CLI -o nupkg
dotnet tool install -g WebHop.CLI --add-source ./nupkg
```

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

The compose file runs the gateway and the example app together:

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
