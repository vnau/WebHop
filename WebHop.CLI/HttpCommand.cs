using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using WebHop.Core;
using WebHop.Server;
using Yarp.ReverseProxy.Forwarder;

namespace WebHop.CLI
{
    /// <summary>
    /// `webhop http 5000`: opens tunnels to the gateway with WebHopServer and forwards every
    /// request that arrives through them to the local server with YARP.
    /// </summary>
    internal static class HttpCommand
    {
        public static async Task<int> RunAsync(HttpOptions options)
        {
            var config = CliConfig.Load(options.ConfigPath);
            var gatewayValue = options.Url ?? Environment.GetEnvironmentVariable("WEBHOP_URL") ?? config.ServerAddr
                ?? throw new CliException("No gateway. Pass --url=https://<your-gateway>/ or run `webhop config add-server-addr https://<your-gateway>/`.");
            var gateway = HttpOptions.ParseGateway(gatewayValue);
            var authToken = options.AuthToken ?? Environment.GetEnvironmentVariable(Constants.AuthTokenEnvironmentVariable) ?? config.AuthToken;
            if (string.IsNullOrWhiteSpace(authToken))
                throw new CliException("No auth token. Run `webhop config add-authtoken <token>`, pass --authtoken or set WEBHOP_AUTHTOKEN.");

            // Like ngrok: the live screen unless logs go to stdout/stderr or output is redirected
            var logTarget = options.Log is LogTarget.None && Console.IsOutputRedirected ? LogTarget.Stdout : options.Log;
            var interactive = logTarget is LogTarget.None or LogTarget.File;
            var session = new Session();

            // No args and a content root outside the current directory: an appsettings.json or
            // ASPNETCORE_* settings meant for the app being exposed must not configure the tunnel
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            ConfigureLogging(builder.Logging, options, logTarget, session);

            builder.WebHost.UseWebHop(gateway.ToString(), o =>
            {
                o.AuthToken = authToken;
                if (options.Connections is { } connections)
                    o.MaxConnections = connections;
            });
            builder.Services.AddHttpForwarder();

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("webhop");
            session.RequestCompleted += entry => LogRequest(logger, entry);
            var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
            var client = CreateClient(options.Target);
            var transformer = new LocalTransformer(options.Target, options.HostHeader);
            var requestConfig = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(120) };
            var target = options.Target.ToString();

            app.Run(async context =>
            {
                var started = Stopwatch.GetTimestamp();
                session.BeginRequest();
                string? failure = null;
                try
                {
                    var error = await forwarder.SendAsync(context, target, client, requestConfig, transformer);
                    if (error != ForwarderError.None)
                    {
                        var exception = context.Features.Get<IForwarderErrorFeature>()?.Exception;
                        failure = exception?.GetBaseException().Message ?? error.ToString();
                        if (error is ForwarderError.Request && !context.Response.HasStarted)
                        {
                            // Like ngrok: say plainly that the local server is the problem
                            context.Response.StatusCode = StatusCodes.Status502BadGateway;
                            context.Response.ContentType = "text/plain; charset=utf-8";
                            await context.Response.WriteAsync($"WebHop could not reach {target}\n\n{failure}\n");
                        }
                    }
                }
                finally
                {
                    session.EndRequest(new RequestEntry(
                        DateTime.Now,
                        context.Request.Method,
                        context.Request.Path + context.Request.QueryString,
                        context.Response.StatusCode,
                        Stopwatch.GetElapsedTime(started),
                        failure));
                }
            });

            try
            {
                await app.StartAsync();
            }
            catch (Exception ex)
            {
                throw new CliException($"Cannot start: {ex.GetBaseException().Message}");
            }

            var server = (WebHopServer)app.Services.GetRequiredService<IServer>();
            var info = new SessionInfo(gateway, options.Target, server);
            logger.LogInformation("Forwarding {PublicUrl} -> {Target}", info.PublicUrl, info.Target);

            if (interactive)
            {
                var dashboard = new Dashboard(info, session);
                var rendering = dashboard.RunAsync(app.Lifetime.ApplicationStopping);
                await app.WaitForShutdownAsync();
                await rendering;
            }
            else
            {
                await app.WaitForShutdownAsync();
            }

            return 0;
        }

        private static void ConfigureLogging(ILoggingBuilder logging, HttpOptions options, LogTarget target, Session session)
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(options.LogLevel);
            // Framework noise only at debug level; YARP's errors already appear on each request's line
            var debug = options.LogLevel <= LogLevel.Debug;
            logging.AddFilter("Microsoft", debug ? LogLevel.Debug : (LogLevel)Math.Max((int)LogLevel.Warning, (int)options.LogLevel));
            logging.AddFilter("Yarp", debug ? LogLevel.Debug : LogLevel.None);

            switch (target)
            {
                case LogTarget.Stdout or LogTarget.Stderr when options.JsonLogs:
                    logging.AddProvider(new LineLoggerProvider(target is LogTarget.Stderr ? Console.Error : Console.Out, json: true));
                    break;

                case LogTarget.Stdout or LogTarget.Stderr:
                    logging.AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                        o.TimestampFormat = "HH:mm:ss ";
                    });
                    logging.Services.Configure<ConsoleLoggerOptions>(o =>
                        o.LogToStandardErrorThreshold = target is LogTarget.Stderr ? LogLevel.Trace : LogLevel.None);
                    break;

                case LogTarget.File:
                    logging.AddProvider(LineLoggerProvider.ForFile(options.LogFile!, options.JsonLogs));
                    logging.AddProvider(new SessionLoggerProvider(session));
                    break;

                default:
                    logging.AddProvider(new SessionLoggerProvider(session));
                    break;
            }
        }

        private static void LogRequest(ILogger logger, RequestEntry entry)
        {
            var reason = ReasonPhrases.GetReasonPhrase(entry.StatusCode);
            var duration = FormatDuration(entry.Duration);
            if (entry.Error is null)
                logger.LogInformation("{Method} {Path} {StatusCode} {Reason} {Duration}", entry.Method, entry.Path, entry.StatusCode, reason, duration);
            else
                logger.LogWarning("{Method} {Path} {StatusCode} {Reason} {Duration} ({Error})", entry.Method, entry.Path, entry.StatusCode, reason, duration, entry.Error);
        }

        public static string FormatDuration(TimeSpan duration) =>
            duration.TotalMilliseconds < 1000 ? $"{duration.TotalMilliseconds:0} ms" : $"{duration.TotalSeconds:0.0} s";

        private static HttpMessageInvoker CreateClient(Uri target)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            };

            // Local dev servers use self-signed certificates (dotnet dev-certs, Vite, ...)
            if (target.IsLoopback)
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            return new HttpMessageInvoker(handler);
        }
    }

    internal sealed record SessionInfo(Uri Gateway, Uri Target, WebHopServer Server)
    {
        public string PublicUrl { get; } = Gateway.GetLeftPart(UriPartial.Authority)
            .Replace("wss://", "https://").Replace("ws://", "http://");
    }

    /// <summary>Sets the Host header the local server sees; forwarded headers pass through unchanged.</summary>
    internal sealed class LocalTransformer(Uri target, string? hostHeader) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext context, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken ct)
        {
            await base.TransformRequestAsync(context, proxyRequest, destinationPrefix, ct);
            proxyRequest.Headers.Host = hostHeader switch
            {
                null => context.Request.Host.Value,
                "rewrite" => target.Authority,
                _ => hostHeader,
            };
        }
    }

    /// <summary>Keeps host errors for the console instead of writing them over it.</summary>
    internal sealed class SessionLoggerProvider(Session session) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            // WebHopServer's connection problems are shown from its Status instead
            categoryName.StartsWith("WebHop.Server", StringComparison.Ordinal) ? NullLogger.Instance : new SessionLogger(session);

        public void Dispose() { }

        private sealed class SessionLogger(Session session) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    session.ReportError(exception?.GetBaseException().Message ?? formatter(state, exception));
            }
        }
    }
}
