namespace InfernalHierarchy.Tools.Options;

public sealed class EmailInboxQueryOptions
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Folder { get; set; } = "INBOX";
    public int TimeoutMs { get; set; } = 15000;
    public int MaxResults { get; set; } = 20;
}
