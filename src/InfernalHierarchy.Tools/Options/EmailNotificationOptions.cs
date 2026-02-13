namespace InfernalHierarchy.Tools.Options;

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Optional default recipient list (comma/semicolon separated) used by the email_send tool
    /// when the caller does not provide a 'to' parameter.
    /// </summary>
    public string? DefaultTo { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    /// <summary>
    /// If true, uses SSL/TLS from connect (e.g., 465). If false, uses STARTTLS when supported.
    /// </summary>
    public bool UseSsl { get; set; } = false;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }

    public int TimeoutMs { get; set; } = 15_000;
}
