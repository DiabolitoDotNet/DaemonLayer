namespace InfernalHierarchy.Host.Configuration;

public sealed class VoiceInterfaceOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// If true, only allow loopback requests (recommended).
    /// </summary>
    public bool LocalOnly { get; set; } = true;

    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
