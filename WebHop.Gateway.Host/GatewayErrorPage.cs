using System.Reflection;
using System.Text;

namespace WebHop.Gateway.Host
{
    /// <summary>
    /// Renders the branded HTML for the gateway's error responses (502/503/504). The markup and the
    /// favicons (an SVG plus a PNG fallback for Safari) live in the embedded <c>ErrorPage</c> resources,
    /// inlined as data URIs so the page is self-contained; only the status code, title and message are
    /// substituted per request.
    /// </summary>
    internal static class GatewayErrorPage
    {
        private static readonly string Template = BuildTemplate();

        public static string Render(int statusCode)
        {
            var (title, message) = statusCode switch
            {
                502 => ("Bad gateway",
                    "The gateway reached your origin, but the tunnel dropped before a response came back. It may have just disconnected — refresh in a moment."),
                503 => ("No origin connected",
                    "The gateway is running, but no origin has opened a tunnel yet. Start your WebHop origin, then refresh this page."),
                504 => ("Gateway timeout",
                    "The origin behind the gateway took too long to respond. It might be busy or stuck — try again shortly."),
                _ => ("Something went wrong",
                    "The gateway could not complete this request."),
            };

            return Template
                .Replace("%CODE%", statusCode.ToString())
                .Replace("%TITLE%", title)
                .Replace("%MESSAGE%", message);
        }

        private static string BuildTemplate()
        {
            return Encoding.UTF8.GetString(ReadResource("gateway-error.html"))
                .Replace("%FAVICON_PNG%", "data:image/png;base64," + Convert.ToBase64String(ReadResource("favicon.png")))
                .Replace("%FAVICON%", "data:image/svg+xml;base64," + Convert.ToBase64String(ReadResource("favicon.svg")))
                .Replace("%HOPPER%", Encoding.UTF8.GetString(ReadResource("hopper-stuck.svg")));
        }

        private static byte[] ReadResource(string suffix)
        {
            var assembly = typeof(GatewayErrorPage).Assembly;
            var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
