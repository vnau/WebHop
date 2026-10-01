namespace WebHop.Tests.Infrastructure
{
    /// <summary>
    /// Tests that change or depend on process-wide environment variables (WEBHOP_AUTHTOKEN).
    /// They run on their own, after the parallel tests.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class EnvironmentVariablesCollection
    {
        public const string Name = "Environment variables";
    }

    /// <summary>Sets environment variables for the duration of a test and restores them afterwards.</summary>
    internal sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> original = [];

        public EnvironmentVariableScope Set(string name, string? value)
        {
            original.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var (name, value) in original)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
