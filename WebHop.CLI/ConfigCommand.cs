using System.Diagnostics;

namespace WebHop.CLI
{
    /// <summary>`webhop config ...`, modelled on `ngrok config ...`.</summary>
    internal static class ConfigCommand
    {
        private static readonly HashSet<string> ValueFlags = ["config"];
        private static readonly HashSet<string> BoolFlags = ["help"];

        public static int Run(IEnumerable<string> args)
        {
            var parsed = CliArgs.Parse(args, ValueFlags, BoolFlags);
            var path = parsed.Get("config") ?? CliConfig.DefaultPath;
            if (parsed.Flag("help"))
                return Help.Config();

            switch (parsed.Positional)
            {
                case ["add-authtoken", var token]:
                    return Update(path, c => c.AuthToken = token, "Auth token");
                case ["add-server-addr", var url]:
                    return Update(path, c => c.ServerAddr = HttpOptions.ParseGateway(url).ToString(), "Server address");
                case ["check"]:
                    return Check(path);
                case ["edit"]:
                    return Edit(path);
                case [] or ["help"]:
                    return Help.Config();
                default:
                    throw new CliException($"Unknown config command: {string.Join(' ', parsed.Positional)}. Run `webhop config help`.");
            }
        }

        private static int Update(string path, Action<CliConfig> change, string what)
        {
            var config = CliConfig.Load(path);
            change(config);
            config.Save(path);
            Console.WriteLine($"{what} saved to configuration file: {path}");
            return 0;
        }

        private static int Check(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"No configuration file at {path}. Create one with `webhop config add-authtoken <token>`.");
                return 1;
            }

            var config = CliConfig.Load(path);
            if (config.ServerAddr is { } serverAddr)
                HttpOptions.ParseGateway(serverAddr);

            Console.WriteLine($"Valid configuration file at {path}");
            Console.WriteLine($"  server_addr  {config.ServerAddr ?? "(not set)"}");
            Console.WriteLine($"  authtoken    {Mask(config.AuthToken)}");
            return 0;
        }

        private static int Edit(string path)
        {
            if (!File.Exists(path))
                new CliConfig().Save(path);

            var editor = Environment.GetEnvironmentVariable("VISUAL") ?? Environment.GetEnvironmentVariable("EDITOR")
                ?? (OperatingSystem.IsWindows() ? "notepad" : "vi");
            using var process = Process.Start(new ProcessStartInfo(editor) { ArgumentList = { path }, UseShellExecute = false })
                ?? throw new CliException($"Cannot start {editor}.");
            process.WaitForExit();
            return process.ExitCode;
        }

        private static string Mask(string? authToken) => authToken switch
        {
            null or "" => "(not set)",
            { Length: <= 4 } => "****",
            _ => authToken[..2] + new string('*', 8),
        };
    }
}
