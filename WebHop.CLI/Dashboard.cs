using Microsoft.AspNetCore.WebUtilities;
using WebHop.Server;

namespace WebHop.CLI
{
    /// <summary>
    /// ngrok-style live console: session status at the top, recent requests below.
    /// Redraws in place, only when something changed.
    /// </summary>
    internal sealed class Dashboard(SessionInfo info, Session session)
    {
        private const int LabelWidth = 30;

        private readonly record struct Segment(string Text, ConsoleColor? Color = null);

        public async Task RunAsync(CancellationToken ct)
        {
            var lastVersion = -1;
            WebHopServerStatus? lastStatus = null;
            var lastSize = (Width: 0, Height: 0);

            TrySetCursorVisible(false);
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                do
                {
                    var status = info.Server.Status;
                    var size = (Width: Console.WindowWidth, Height: Console.WindowHeight);
                    if (size != lastSize)
                    {
                        Console.Clear();
                        lastVersion = -1;
                        lastSize = size;
                    }

                    if (session.Version != lastVersion || status != lastStatus)
                    {
                        lastVersion = session.Version;
                        lastStatus = status;
                        Render(Build(status, size.Height), size.Width, size.Height);
                    }
                }
                while (await WaitAsync(timer, ct));
            }
            finally
            {
                Console.ResetColor();
                Console.Clear();
                TrySetCursorVisible(true);
                Console.WriteLine($"WebHop session closed ({session.Total} requests).");
            }
        }

        private List<Segment[]> Build(WebHopServerStatus status, int height)
        {
            List<Segment[]> lines =
            [
                [new("WebHop", ConsoleColor.Cyan), new("                                                (Ctrl+C to quit)", ConsoleColor.DarkGray)],
                [],
                Row("Session Status", StatusText(status)),
                Row("Version", new Segment(AppVersion.Current)),
                Row("Server Id", new Segment(info.Server.ServerId)),
                Row("Tunnels", new Segment($"{status.OpenTunnels}/{status.MaxTunnels} open")),
                Row("Forwarding", new Segment($"{info.PublicUrl} -> {info.Target.ToString().TrimEnd('/')}")),
            ];

            if (status.LastError is { } error)
                lines.Add(Row(status.State == WebHopConnectionState.Online ? "Warning" : "Error",
                    new Segment(error, status.State == WebHopConnectionState.Online ? ConsoleColor.Yellow : ConsoleColor.Red)));
            if (info.Gateway.Scheme is "http" or "ws" && !info.Gateway.IsLoopback)
                lines.Add(Row("Warning", new Segment("the auth token is sent unencrypted; use an https:// gateway", ConsoleColor.Yellow)));
            if (session.LastError is { } hostError)
                lines.Add(Row("Last Error", new Segment(hostError, ConsoleColor.Red)));

            var percentiles = session.Percentiles();
            lines.Add([]);
            lines.Add(Row("Connections", new Segment($"{"ttl",-8}{"opn",-8}{"p50",-10}{"p90",-10}", ConsoleColor.DarkGray)));
            lines.Add(Row("", new Segment($"{session.Total,-8}{session.InFlight,-8}{Ms(percentiles?.P50),-10}{Ms(percentiles?.P90),-10}")));
            lines.Add([]);
            lines.Add([new("HTTP Requests")]);
            lines.Add([new("-------------")]);

            foreach (var entry in session.RecentRequests(Math.Max(0, height - lines.Count - 1)))
            {
                lines.Add([
                    new($"{entry.Time:HH:mm:ss}  ", ConsoleColor.DarkGray),
                    new($"{entry.Method,-7} "),
                    new($"{Truncate(entry.Path, 40),-41} "),
                    new($"{entry.StatusCode} {ReasonPhrases.GetReasonPhrase(entry.StatusCode),-22} ", StatusColor(entry.StatusCode)),
                    new($"{HttpCommand.FormatDuration(entry.Duration),8}", ConsoleColor.DarkGray),
                    new(entry.Error is null ? "" : $"  {entry.Error}", ConsoleColor.Red),
                ]);
            }

            return lines;
        }

        private static void Render(List<Segment[]> lines, int width, int height)
        {
            // Stay one column short of the edge so a full line never wraps and scrolls the screen
            var usable = Math.Max(1, width - 1);
            for (var row = 0; row < height - 1; row++)
            {
                Console.SetCursorPosition(0, row);
                var written = 0;
                if (row < lines.Count)
                {
                    foreach (var segment in lines[row])
                    {
                        if (written >= usable)
                            break;
                        var text = segment.Text.Length > usable - written ? segment.Text[..(usable - written)] : segment.Text;
                        if (segment.Color is { } color)
                            Console.ForegroundColor = color;
                        Console.Write(text);
                        Console.ResetColor();
                        written += text.Length;
                    }
                }
                Console.Write(new string(' ', usable - written));
            }
        }

        private static Segment[] Row(string label, params Segment[] value) =>
            [new(label.PadRight(LabelWidth)), .. value];

        private static Segment StatusText(WebHopServerStatus status) => status.State switch
        {
            WebHopConnectionState.Online => new("online", ConsoleColor.Green),
            WebHopConnectionState.Connecting => new("connecting", ConsoleColor.Yellow),
            WebHopConnectionState.Offline => new("reconnecting", ConsoleColor.Red),
            WebHopConnectionState.Unauthorized => new("unauthorized - check the auth token", ConsoleColor.Red),
            _ => new(status.State.ToString()),
        };

        private static ConsoleColor StatusColor(int statusCode) => statusCode switch
        {
            < 300 => ConsoleColor.Green,
            < 400 => ConsoleColor.Cyan,
            < 500 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };

        private static string Ms(double? milliseconds) =>
            milliseconds is { } ms ? HttpCommand.FormatDuration(TimeSpan.FromMilliseconds(ms)) : "-";

        private static string Truncate(string value, int length) =>
            value.Length <= length ? value : value[..(length - 1)] + "…";

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try
            {
                return await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static void TrySetCursorVisible(bool visible)
        {
            try
            {
                Console.CursorVisible = visible;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
