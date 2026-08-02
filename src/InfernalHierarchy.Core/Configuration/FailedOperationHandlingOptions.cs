namespace InfernalHierarchy.Core.Configuration;

public sealed class FailedOperationHandlingOptions
{
    public bool Enabled { get; set; } = true;

    public int ReplayRetryBudget { get; set; } = 3;

    public int MaxEntries { get; set; } = 5000;

    /// <summary>
    /// Optional dedicated database path for failed operations.
    /// If empty, a file is created next to Memory:DatabasePath.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Enables autonomous background replay of pending failed operations.
    /// </summary>
    public bool AutonomousReplayEnabled { get; set; } = true;

    /// <summary>
    /// Number of pending entries scanned per worker loop.
    /// </summary>
    public int ReplayBatchSize { get; set; } = 20;

    /// <summary>
    /// Idle delay between replay loops in milliseconds.
    /// </summary>
    public int ReplayPollIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Base backoff delay in milliseconds after a failed replay attempt.
    /// </summary>
    public int ReplayInitialBackoffMs { get; set; } = 250;

    /// <summary>
    /// Maximum exponential backoff delay in milliseconds.
    /// </summary>
    public int ReplayMaxBackoffMs { get; set; } = 30000;

    /// <summary>
    /// Jitter ratio applied to replay backoff delay (0-1 range).
    /// </summary>
    public double ReplayJitterRatio { get; set; } = 0.15;

    /// <summary>
    /// Hard cap on replay attempts per worker loop to avoid replay storms.
    /// </summary>
    public int ReplayMaxAttemptsPerLoop { get; set; } = 10;
}
