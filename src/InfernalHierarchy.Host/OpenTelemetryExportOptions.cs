namespace InfernalHierarchy.Host;

/// <summary>
/// OpenTelemetry exporter configuration.
/// </summary>
public sealed class OpenTelemetryExportOptions
{
    public ConsoleExporterOptions Console { get; set; } = new();

    public OtlpExporterOptions Otlp { get; set; } = new();
}

public sealed class ConsoleExporterOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class OtlpExporterOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OTLP endpoint (typically gRPC: http://localhost:4317).
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:4317";
}
