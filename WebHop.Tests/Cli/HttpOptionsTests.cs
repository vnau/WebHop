using WebHop.CLI;

namespace WebHop.Tests.Cli
{
    public class HttpOptionsTests
    {
        [Theory]
        [InlineData("8080", "http://localhost:8080/")]
        [InlineData("localhost:3000", "http://localhost:3000/")]
        [InlineData("app.local:8081", "http://app.local:8081/")]
        [InlineData("https://localhost:7001", "https://localhost:7001/")]
        [InlineData("https://localhost:7001/ignored/path?q=1", "https://localhost:7001/")]
        public void Targets_are_accepted_like_ngrok(string value, string expected)
        {
            Assert.Equal(expected, HttpOptions.ParseTarget(value).AbsoluteUri);
        }

        [Theory]
        [InlineData("ftp://localhost:21")]
        [InlineData("http://")]
        public void Invalid_targets_are_rejected(string value)
        {
            Assert.Throws<CliException>(() => HttpOptions.ParseTarget(value));
        }

        [Theory]
        [InlineData("https://gw.example.com/")]
        [InlineData("http://localhost:5199")]
        [InlineData("wss://gw.example.com/webhop")]
        public void Gateway_urls_must_be_http_or_websocket(string value)
        {
            Assert.Equal(new Uri(value), HttpOptions.ParseGateway(value));
        }

        [Fact]
        public void A_gateway_that_is_not_a_url_is_rejected()
        {
            Assert.Throws<CliException>(() => HttpOptions.ParseGateway("my-gateway"));
        }

        [Fact]
        public void Defaults_show_the_live_screen_at_info_level()
        {
            var options = HttpOptions.Parse(["8080"])!;

            Assert.Equal(LogTarget.None, options.Log);
            Assert.Equal(LogLevel.Information, options.LogLevel);
            Assert.False(options.JsonLogs);
            Assert.Equal(CliConfig.DefaultPath, options.ConfigPath);
            Assert.Null(options.Url);
            Assert.Null(options.AuthToken);
        }

        [Fact]
        public void All_flags_are_read()
        {
            var options = HttpOptions.Parse([
                "3000", "--url=https://gw/", "--authtoken", "tok", "--host-header=rewrite",
                "--connections", "4", "--log-level=warn", "--log-format=json", "--config", "my.json",
            ])!;

            Assert.Equal("http://localhost:3000/", options.Target.AbsoluteUri);
            Assert.Equal("https://gw/", options.Url);
            Assert.Equal("tok", options.AuthToken);
            Assert.Equal("rewrite", options.HostHeader);
            Assert.Equal(4, options.Connections);
            Assert.Equal(LogLevel.Warning, options.LogLevel);
            Assert.True(options.JsonLogs);
            Assert.Equal("my.json", options.ConfigPath);
        }

        [Theory]
        [InlineData("stdout", "Stdout")]
        [InlineData("stderr", "Stderr")]
        [InlineData("false", "None")]
        [InlineData("webhop.log", "File")]
        public void The_log_flag_selects_where_logs_go(string value, string target)
        {
            var expected = Enum.Parse<LogTarget>(target);
            var options = HttpOptions.Parse(["8080", $"--log={value}"])!;

            Assert.Equal(expected, options.Log);
            Assert.Equal(expected == LogTarget.File ? Path.GetFullPath(value) : null, options.LogFile);
        }

        [Fact]
        public void Help_returns_no_options()
        {
            Assert.Null(HttpOptions.Parse(["--help"]));
            Assert.Null(HttpOptions.Parse(["8080", "-h"]));
        }

        [Theory]
        [InlineData("")]
        [InlineData("8080 9090")]
        [InlineData("8080 --connections 0")]
        [InlineData("8080 --log-level=loud")]
        [InlineData("8080 --log-format=xml")]
        public void Invalid_arguments_are_rejected(string commandLine)
        {
            var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Throws<CliException>(() => HttpOptions.Parse(args));
        }
    }
}
