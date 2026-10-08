using System.Text.Json;
using WebHop.CLI;

namespace WebHop.Tests.Cli
{
    public sealed class CliConfigTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "webhop-tests-" + Guid.NewGuid().ToString("N"));
        private string ConfigPath => Path.Combine(directory, "nested", "webhop.json");

        [Fact]
        public void Settings_survive_a_save_and_load()
        {
            new CliConfig { AuthToken = "tok", GatewayUrl = "https://gw/" }.Save(ConfigPath);

            var loaded = CliConfig.Load(ConfigPath);

            Assert.Equal("tok", loaded.AuthToken);
            Assert.Equal("https://gw/", loaded.GatewayUrl);
        }

        [Fact]
        public void The_file_uses_ngrok_style_keys_and_omits_unset_values()
        {
            new CliConfig { AuthToken = "tok" }.Save(ConfigPath);

            using var json = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            var keys = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(["authtoken"], keys);
        }

        [Fact]
        public void A_missing_file_means_no_settings()
        {
            var loaded = CliConfig.Load(ConfigPath);

            Assert.Null(loaded.AuthToken);
            Assert.Null(loaded.GatewayUrl);
        }

        [Fact]
        public void A_broken_file_is_reported()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, "{ not json");

            Assert.Throws<CliException>(() => CliConfig.Load(ConfigPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
