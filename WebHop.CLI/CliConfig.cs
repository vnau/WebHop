using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebHop.CLI
{
    /// <summary>
    /// Per-user settings written by `webhop config add-authtoken` / `add-server-addr`, like ngrok's config file:
    /// %APPDATA%\webhop\webhop.json on Windows, ~/.config/webhop/webhop.json elsewhere, or --config.
    /// </summary>
    internal sealed class CliConfig
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>The gateway's auth token (key named as in ngrok's config).</summary>
        [JsonPropertyName("authtoken")]
        public string? AuthToken { get; set; }

        /// <summary>Gateway to connect to when --url is not given (ngrok's server_addr).</summary>
        [JsonPropertyName("server_addr")]
        public string? ServerAddr { get; set; }

        public static string DefaultPath { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "webhop", "webhop.json");

        public static CliConfig Load(string path)
        {
            if (!File.Exists(path))
                return new CliConfig();

            try
            {
                return JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(path), JsonOptions) ?? new CliConfig();
            }
            catch (JsonException ex)
            {
                throw new CliException($"Cannot read {path}: {ex.Message}");
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

            // The file holds the auth token: readable by the user only
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>An error shown to the user without a stack trace.</summary>
    internal sealed class CliException(string message) : Exception(message);
}
