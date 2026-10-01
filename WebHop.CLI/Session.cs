using System.Collections.Concurrent;

namespace WebHop.CLI
{
    internal sealed record RequestEntry(DateTime Time, string Method, string Path, int StatusCode, TimeSpan Duration, string? Error);

    /// <summary>Request statistics and the recent request list shown by the console.</summary>
    internal sealed class Session
    {
        private const int KeepRequests = 200;
        private const int KeepDurations = 1000;

        private readonly ConcurrentQueue<RequestEntry> requests = new();
        private readonly double[] durations = new double[KeepDurations];
        private readonly object sync = new();
        private int durationCount;
        private int durationNext;
        private int total;
        private int inFlight;
        private int version;

        public event Action<RequestEntry>? RequestCompleted;

        public int Total => Volatile.Read(ref total);
        public int InFlight => Volatile.Read(ref inFlight);

        /// <summary>Changes whenever something shown on screen changes.</summary>
        public int Version => Volatile.Read(ref version);

        /// <summary>Last unexpected error logged by the host, if any.</summary>
        public string? LastError { get; private set; }

        public void BeginRequest()
        {
            Interlocked.Increment(ref inFlight);
            Interlocked.Increment(ref version);
        }

        public void EndRequest(RequestEntry entry)
        {
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref total);

            requests.Enqueue(entry);
            while (requests.Count > KeepRequests && requests.TryDequeue(out _)) { }

            lock (sync)
            {
                durations[durationNext] = entry.Duration.TotalMilliseconds;
                durationNext = (durationNext + 1) % KeepDurations;
                durationCount = Math.Min(durationCount + 1, KeepDurations);
            }

            Interlocked.Increment(ref version);
            RequestCompleted?.Invoke(entry);
        }

        public void ReportError(string message)
        {
            LastError = message;
            Interlocked.Increment(ref version);
        }

        /// <summary>Most recent first.</summary>
        public IReadOnlyList<RequestEntry> RecentRequests(int count) =>
            requests.Reverse().Take(count).ToList();

        /// <summary>Duration percentiles over the last requests, in milliseconds.</summary>
        public (double P50, double P90)? Percentiles()
        {
            double[] sample;
            lock (sync)
            {
                if (durationCount == 0)
                    return null;
                sample = durations[..durationCount];
            }

            Array.Sort(sample);
            return (sample[(int)(0.5 * (sample.Length - 1))], sample[(int)(0.9 * (sample.Length - 1))]);
        }
    }
}
