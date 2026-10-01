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

        public bool IsAuthorized(HttpRequest request)
        {
            // No open mode: without an auth token anyone could register as a server and receive the traffic
            if (authTokenHash is null)
                return false;

            const string scheme = "Bearer ";
            var header = request.Headers.Authorization.ToString();
            if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return false;

            // Comparing hashes keeps the comparison constant-time regardless of the token's length
            var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[scheme.Length..].Trim()));
            return CryptographicOperations.FixedTimeEquals(presented, authTokenHash);
        }
    }
}
