namespace WebHop.Core
{
    public static class Constants
    {
        public const string DefaultWebHopEndpoint = "/webhop";
        public const string WebHopProtoVersion = "2.0";

        /// <summary>Query string parameter a server uses to identify itself when opening a tunnel.</summary>
        public const string ServerIdParameter = "id";

        /// <summary>Environment variable holding the auth token, read by the gateway, apps and the CLI alike.</summary>
        public const string AuthTokenEnvironmentVariable = "WEBHOP_AUTHTOKEN";

        /// <summary>
        /// Configuration key for the auth token (appsettings, user secrets, Azure app settings), read by the
        /// gateway and apps. Takes precedence over <see cref="AuthTokenEnvironmentVariable"/>.
        /// </summary>
        public const string AuthTokenSetting = "WebHop:AuthToken";
    }
}
