namespace WebHop.CLI
{
    internal enum LogTarget { None, Stdout, Stderr, File }

    internal sealed class HttpOptions
    {
        private static readonly HashSet<string> ValueFlags =
            ["url", "authtoken", "host-header", "connections", "log", "log-level", "log-format", "config"];

        private static readonly HashSet<string> BoolFlags = ["help"];

        public required Uri Target { get; init; }
        public string? Url { get; init; }
        public string? AuthToken { get; init; }
        public string? HostHeader { get; init; }
        public int? Connections { get; init; }
        public LogTarget Log { get; init; }
        public string? LogFile { get; init; }
        public LogLevel LogLevel { get; init; } = LogLevel.Information;
        public bool JsonLogs { get; init; }
        public string ConfigPath { get; init; } = CliConfig.DefaultPath;

        /// <summary>Null when --help was asked for.</summary>
        public static HttpOptions? Parse(IEnumerable<string> args)
        {
            var parsed = CliArgs.Parse(args, ValueFlags, BoolFlags);
            if (parsed.Flag("help"))
                return null;

            var target = parsed.Positional switch
            {
                [var single] => single,
                [] => throw new CliException("Which local server? For example: webhop http 8080"),
                _ => throw new CliException($"Unexpected argument {parsed.Positional[1]}."),
            };

            var log = parsed.Get("log");
            return new HttpOptions
            {
                Target = ParseTarget(target),
                Url = parsed.Get("url"),
                AuthToken = parsed.Get("authtoken"),
                HostHeader = parsed.Get("host-header"),
                Connections = parsed.Get("connections") is { } c
                    ? int.TryParse(c, out var n) && n > 0 ? n : throw new CliException("--connections needs a positive number.")
                    : null,
                Log = log switch
                {
                    null or "false" => LogTarget.None,
                    "stdout" => LogTarget.Stdout,
                    "stderr" => LogTarget.Stderr,
                    _ => LogTarget.File,
                },
                LogFile = log is null or "false" or "stdout" or "stderr" ? null : Path.GetFullPath(log),
                LogLevel = parsed.Get("log-level") switch
                {
                    null or "info" => LogLevel.Information,
                    "debug" => LogLevel.Debug,
                    "warn" => LogLevel.Warning,
                    "error" => LogLevel.Error,
                    "crit" => LogLevel.Critical,
                    var other => throw new CliException($"--log-level must be debug, info, warn, error or crit, not `{other}`."),
                },
                JsonLogs = parsed.Get("log-format") switch
                {
                    null or "term" or "logfmt" => false,
                    "json" => true,
                    var other => throw new CliException($"--log-format must be term or json, not `{other}`."),
                },
                ConfigPath = parsed.Get("config") ?? CliConfig.DefaultPath,
            };
        }

        /// <summary>Accepts 8080, localhost:8080 or a full http(s) URL, like ngrok.</summary>
        public static Uri ParseTarget(string value)
        {
            if (int.TryParse(value, out var port) && port is > 0 and <= 65535)
                return new Uri($"http://localhost:{port}/");

            if (!value.Contains("://"))
                value = "http://" + value;

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");

            throw new CliException($"`{value}` is not a port, host:port or http(s) URL.");
        }

        public static Uri ParseGateway(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ws" or "wss")
                return uri;

            throw new CliException($"`{value}` is not a gateway URL, for example https://my-gateway.azurewebsites.net/");
        }
    }
}
