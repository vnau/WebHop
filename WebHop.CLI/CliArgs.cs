namespace WebHop.CLI
{
    /// <summary>
    /// ngrok-style arguments: positional words plus --flag value, --flag=value or -flag value,
    /// in any order. Boolean flags take no value unless written as --flag=true|false.
    /// </summary>
    internal sealed class CliArgs
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public List<string> Positional { get; } = [];

        public static CliArgs Parse(IEnumerable<string> args, IReadOnlySet<string> valueFlags, IReadOnlySet<string> boolFlags)
        {
            var result = new CliArgs();
            using var e = args.GetEnumerator();
            while (e.MoveNext())
            {
                var arg = e.Current;
                if (arg is "--" or "-" || !arg.StartsWith('-'))
                {
                    result.Positional.Add(arg);
                    continue;
                }

                var name = arg.TrimStart('-');
                string? value = null;
                if (name.IndexOf('=') is var eq and > 0)
                {
                    value = name[(eq + 1)..];
                    name = name[..eq];
                }

                if (name is "h" or "help")
                    name = "help";

                if (boolFlags.Contains(name))
                {
                    result.values[name] = value ?? "true";
                }
                else if (valueFlags.Contains(name))
                {
                    if (value is null)
                        value = e.MoveNext() ? e.Current : throw new CliException($"--{name} needs a value.");
                    result.values[name] = value;
                }
                else
                {
                    throw new CliException($"Unknown flag --{name}. Run `webhop help`.");
                }
            }

            return result;
        }

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Flag(string name) => values.TryGetValue(name, out var value) && value is not ("false" or "0");
    }
}
