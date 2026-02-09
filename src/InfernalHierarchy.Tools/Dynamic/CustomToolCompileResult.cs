namespace InfernalHierarchy.Tools.Dynamic;

public sealed record CustomToolCompileResult(
    bool Success,
    ITool? Tool,
    string? Error,
    IReadOnlyList<string> Diagnostics);
