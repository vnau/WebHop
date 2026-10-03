using System.Security.Cryptography;
using System.Text;
using WebHop.Core;

namespace WebHop.Gateway
{
    /// <summary>
    /// Checks the auth token servers present when opening a tunnel (Authorization: Bearer &lt;token&gt;).
    /// Only tunnel registration is protected; public traffic to the gateway stays open.
    /// Without a configured auth token the gateway still starts, but accepts no tunnels at all.
    /// </summary>
    public sealed class TunnelAuthorization
    {
        private readonly byte[]? authTokenHash;

        public TunnelAuthorization(IConfiguration configuration)
        {
            // Same order as apps using UseWebHop: the setting first, then the environment variable
            var authToken = configuration[Constants.AuthTokenSetting];
            if (string.IsNullOrWhiteSpace(authToken))
                authToken = Environment.GetEnvironmentVariable(Constants.AuthTokenEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(authToken))
                authTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(authToken));
        }

        public bool IsConfigured => authTokenHash is not null;

        /// <summary>Checks the <c>Authorization: Bearer</c> header, or a token carried in the subprotocols.</summary>
        public bool IsAuthorized(HttpContext context)
        {
            const string scheme = "Bearer ";
            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
                && IsAuthorized(header[scheme.Length..].Trim()))
                return true;

            // Browsers cannot set Authorization on a WebSocket, so a token may ride in Sec-WebSocket-Protocol
            return IsAuthorized(TokenFromSubprotocols(context.WebSockets.WebSocketRequestedProtocols));
        }

        /// <summary>Constant-time check of a presented token against the configured one.</summary>
        public bool IsAuthorized(string? token)
        {
            // No open mode: without an auth token anyone could register as a server and receive the traffic
            if (authTokenHash is null || string.IsNullOrEmpty(token))
                return false;

            // Comparing hashes keeps the comparison constant-time regardless of the token's length
            var presented = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return CryptographicOperations.FixedTimeEquals(presented, authTokenHash);
        }

        /// <summary>Extracts the base64url token from a <c>webhop.token.*</c> subprotocol, or null.</summary>
        public static string? TokenFromSubprotocols(IEnumerable<string> protocols)
        {
            foreach (var protocol in protocols)
            {
                if (!protocol.StartsWith(Constants.TokenSubprotocolPrefix, StringComparison.Ordinal))
                    continue;
                try
                {
                    return Encoding.UTF8.GetString(Base64UrlDecode(protocol[Constants.TokenSubprotocolPrefix.Length..]));
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            return null;
        }

        private static byte[] Base64UrlDecode(string value)
        {
            value = value.Replace('-', '+').Replace('_', '/');
            value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            return Convert.FromBase64String(value);
        }
    }
}
