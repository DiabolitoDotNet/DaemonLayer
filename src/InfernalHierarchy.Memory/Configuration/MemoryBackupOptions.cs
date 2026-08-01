namespace InfernalHierarchy.Memory.Configuration;

public sealed class MemoryBackupOptions
{
    public bool Enabled { get; set; }

    public bool BackupOnStartup { get; set; } = true;

    public double IntervalHours { get; set; } = 24;

    public string DirectoryPath { get; set; } = "data/backups";

    public int MaxBackupFiles { get; set; } = 7;

    public int MaxBackupAgeDays { get; set; } = 14;
}