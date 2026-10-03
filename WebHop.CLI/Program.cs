using System.Reflection;
using WebHop.CLI;

try
{
    return CommandFirst(args) switch
    {
        ["http", .. var rest] => HttpOptions.Parse(rest) is { } options ? await HttpCommand.RunAsync(options) : Help.Http(),
        ["config", .. var rest] => ConfigCommand.Run(rest),
        ["version" or "--version" or "-v"] => Help.Version(),
        ["help", "http"] => Help.Http(),
        ["help", "config"] => Help.Config(),
        [] or ["help" or "--help" or "-h", ..] => Help.General(),
        _ => throw new CliException($"Unknown command: {args[0]}. Run `webhop help`."),
    };
}
catch (CliException ex)
{
    Console.Error.WriteLine($"ERROR:  {ex.Message}");
    return 1;
}

// Like ngrok, flags may come before the command (webhop --config x.json http 8080)
static string[] CommandFirst(string[] args)
{
    string[] valueFlags = ["url", "authtoken", "host-header", "connections", "log", "log-level", "log-format", "config"];
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith('-'))
            return i == 0 ? args : [arg, .. args[..i], .. args[(i + 1)..]];
        // Skip a flag's separate value, so `--log http` is not mistaken for the command
        if (!arg.Contains('=') && valueFlags.Contains(arg.TrimStart('-')))
            i++;
    }
    return args;
}

internal static class AppVersion
{
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}

internal static class Help
{
    public static int Version()
    {
        Console.WriteLine($"webhop version {AppVersion.Current}");
        return 0;
    }

    public static int General()
    {
        Console.WriteLine($"""
            NAME:
              webhop - expose a local server through a WebHop gateway

            USAGE:
              webhop [command] [flags]

            COMMANDS:
              http       start an HTTP tunnel to a local server
              config     update or inspect the configuration file
              version    print the version
              help       help about any command

            EXAMPLES:
              webhop config add-server-addr https://my-gateway.azurewebsites.net/
              webhop config add-authtoken <token>
              webhop http 8080

            Run `webhop help <command>` for details. Version {AppVersion.Current}.
            """);
        return 0;
    }

    public static int Http()
    {
        Console.WriteLine($"""
            NAME:
              http - start an HTTP tunnel to a local server

            USAGE:
              webhop http [address:port | port | url] [flags]

            EXAMPLES:
              webhop http 8080                             forward to http://localhost:8080
              webhop http app.local:80                     forward to another host on your network
              webhop http https://localhost:7001           forward to a local https server (self-signed is fine)
              webhop http 3000 --host-header=rewrite       send Host: localhost:3000 (Vite, webpack dev server)
              webhop http 8080 --url=https://gw.example.com/ --authtoken=<token>
              webhop http 8080 --log=stdout                log lines instead of the live screen

            FLAGS:
              --url string           gateway URL, which is also the public URL
                                     (default: WEBHOP_URL, then server_addr from the config file)
              --authtoken string     the gateway's auth token (default: WEBHOP_AUTHTOKEN,
                                     then authtoken from the config file)
              --host-header string   Host header sent to the local server: `rewrite` for the
                                     target's host, or any value (default: the public host)
              --connections int      tunnels to keep open; caps concurrent requests (default 10)
              --log string           where to write logs: stdout, stderr, false or a file path.
                                     stdout/stderr replace the live screen (default false)
              --log-level string     debug, info, warn, error or crit (default info)
              --log-format string    term or json (default term)
              --config string        path to the config file (default {CliConfig.DefaultPath})
              -h, --help             help for http
            """);
        return 0;
    }

    public static int Config()
    {
        Console.WriteLine($"""
            NAME:
              config - update or inspect the configuration file

            USAGE:
              webhop config add-authtoken <token>      save the gateway's auth token
              webhop config add-server-addr <url>      save the gateway to connect to by default
              webhop config check                      validate the file and show its settings
              webhop config edit                       open the file in $VISUAL, $EDITOR or notepad/vi

            FLAGS:
              --config string    path to the config file (default {CliConfig.DefaultPath})
            """);
        return 0;
    }
}
