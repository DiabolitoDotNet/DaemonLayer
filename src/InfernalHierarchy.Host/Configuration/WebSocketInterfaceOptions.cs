namespace InfernalHierarchy.Host.Configuration;

/// <summary>
/// Configuration for the built-in WebSocket interface (served under /ws).
/// </summary>
public sealed class WebSocketInterfaceOptions
{
    /// <summary>
    /// Whether the /ws endpoint is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, only loopback clients may connect.
    /// </summary>
    public bool LocalOnly { get; set; } = true;

    /// <summary>
    /// Maximum size for a single client-to-server message frame.
    /// </summary>
    public int MaxClientMessageBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Keep-alive ping interval.
    /// </summary>
    public int KeepAliveSeconds { get; set; } = 30;
}
