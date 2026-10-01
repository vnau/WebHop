using WebHop.CLI;

namespace WebHop.Tests.Cli
{
    public class CliArgsTests
    {
        private static readonly HashSet<string> ValueFlags = ["url", "authtoken", "log"];
        private static readonly HashSet<string> BoolFlags = ["help"];

        [Fact]
        public void Flags_work_in_all_ngrok_forms_and_any_order()
        {
            var args = CliArgs.Parse(["--url", "https://gw/", "8080", "--authtoken=tok", "-log", "stdout"], ValueFlags, BoolFlags);

            Assert.Equal(["8080"], args.Positional);
            Assert.Equal("https://gw/", args.Get("url"));
            Assert.Equal("tok", args.Get("authtoken"));
            Assert.Equal("stdout", args.Get("log"));
        }

        [Fact]
        public void A_value_after_equals_may_contain_equals_signs()
        {
            var args = CliArgs.Parse(["--authtoken=a=b=c"], ValueFlags, BoolFlags);

            Assert.Equal("a=b=c", args.Get("authtoken"));
        }

        [Theory]
        [InlineData("--help", true)]
        [InlineData("-h", true)]
        [InlineData("--help=true", true)]
        [InlineData("--help=false", false)]
        public void Boolean_flags_take_no_value_unless_given_with_equals(string flag, bool expected)
        {
            var args = CliArgs.Parse([flag, "8080"], ValueFlags, BoolFlags);

            Assert.Equal(expected, args.Flag("help"));
            Assert.Equal(["8080"], args.Positional);
        }

        [Fact]
        public void Unknown_flags_are_rejected()
        {
            var error = Assert.Throws<CliException>(() => CliArgs.Parse(["--bogus"], ValueFlags, BoolFlags));
            Assert.Contains("--bogus", error.Message);
        }

        [Fact]
        public void A_value_flag_without_a_value_is_rejected()
        {
            Assert.Throws<CliException>(() => CliArgs.Parse(["8080", "--url"], ValueFlags, BoolFlags));
        }

        [Fact]
        public void Missing_flags_read_as_null_and_false()
        {
            var args = CliArgs.Parse(["8080"], ValueFlags, BoolFlags);

            Assert.Null(args.Get("url"));
            Assert.False(args.Flag("help"));
        }
    }
}
