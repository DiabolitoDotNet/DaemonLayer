namespace InfernalHierarchy.Tools.Options;

public sealed class CodeExecutionToolOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Root directory that all code execution is sandboxed to (working directory).
    /// Can be absolute, or relative to the Host content root.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    public bool EnablePython { get; set; } = false;
    public bool EnableNode { get; set; } = false;

    public string PythonExecutable { get; set; } = "python";
    public string NodeExecutable { get; set; } = "node";

    /// <summary>
    /// Hard upper bound; per-request overrides may be lower but not higher.
    /// </summary>
    public int TimeoutMs { get; set; } = 15_000;

    /// <summary>
    /// Hard upper bound; per-request overrides may be lower but not higher.
    /// </summary>
    public int MaxOutputBytes { get; set; } = 256_000;

    public int MaxCodeChars { get; set; } = 12_000;
}
