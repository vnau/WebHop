using System.Text.Json;

namespace WebHop.CLI
{
    /// <summary>
    /// Writes one line per log entry: --log=&lt;file&gt; (next to the live screen) and
    /// --log-format=json on stdout/stderr. JSON lines use ngrok's field names (t, lvl, obj, msg).
    /// </summary>
    internal sealed class LineLoggerProvider : ILoggerProvider
    {
        private readonly TextWriter writer;
        private readonly bool ownsWriter;
        private readonly bool json;
        private readonly object sync = new();

        public LineLoggerProvider(TextWriter writer, bool json)
        {
            this.writer = writer;
            this.json = json;
        }

        public static LineLoggerProvider ForFile(string path, bool json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                return new LineLoggerProvider(new StreamWriter(stream) { AutoFlush = true }, json, ownsWriter: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CliException($"Cannot write log file {path}: {ex.Message}");
            }
        }

        private LineLoggerProvider(TextWriter writer, bool json, bool ownsWriter) : this(writer, json)
        {
            this.ownsWriter = ownsWriter;
        }

        public ILogger CreateLogger(string categoryName) => new LineLogger(this, categoryName);

        public void Dispose()
        {
            if (ownsWriter)
                lock (sync)
                    writer.Dispose();
        }

        private void Write(LogLevel level, string category, string message, Exception? exception)
        {
            var now = DateTimeOffset.Now;
            var line = json
                ? JsonSerializer.Serialize(new LogLine(now.ToString("O"), LevelName(level), category, message, exception?.Message), LogLineJsonContext.Default.LogLine)
                : $"{now:yyyy-MM-dd HH:mm:ss.fff} {LevelName(level)} {category}: {message}" + (exception is null ? "" : $" ({exception.Message})");

            lock (sync)
                writer.WriteLine(line);
        }

        private static string LevelName(LogLevel level) => level switch
        {
            LogLevel.Trace or LogLevel.Debug => "debug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "error",
            _ => "crit",
        };

        private sealed class LineLogger(LineLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
