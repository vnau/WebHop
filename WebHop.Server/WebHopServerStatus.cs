namespace WebHop.Server
{
    public enum WebHopConnectionState
    {
        /// <summary>No tunnel has been opened yet.</summary>
        Connecting,

        /// <summary>At least one tunnel is open; the app is reachable through the gateway.</summary>
        Online,

        /// <summary>The gateway cannot be reached; retrying.</summary>
        Offline,

        /// <summary>The gateway rejected the auth token; retrying.</summary>
        Unauthorized,
    }

    /// <summary>Snapshot of the tunnels to the gateway.</summary>
    /// <param name="LastError">Why the last attempt to open a tunnel failed, while that is still relevant.</param>
    public sealed record WebHopServerStatus(WebHopConnectionState State, int OpenTunnels, int MaxTunnels, string? LastError);
}
