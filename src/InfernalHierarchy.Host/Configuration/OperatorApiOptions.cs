namespace InfernalHierarchy.Host.Configuration;

public sealed class OperatorApiOptions
{
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Shared secret used to authorize operator endpoints.
    /// Provide via user-secrets or environment variables.
    /// Header: X-Infernal-Operator-Key
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}
