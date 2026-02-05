using System.Text.Json;

namespace InfernalHierarchy.Core.Serialization;

/// <summary>
/// Shared JSON serialization defaults used across Host/Agents/Tools.
/// Treat returned options as read-only.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web);

    public static JsonSerializerOptions WebIndented { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static JsonSerializerOptions WebCaseInsensitive { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static JsonSerializerOptions WebCaseInsensitiveIndented { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
