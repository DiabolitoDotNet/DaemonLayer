
namespace InfernalHierarchy.Tools.Tools.CodeExecution;

internal sealed class CodeExecutionSandbox
{
    private readonly CodeExecutionToolOptions _options;
    private readonly ILogger _logger;
    private readonly string _rootFullPath;

    public string RootFullPath => _rootFullPath;

    public CodeExecutionSandbox(CodeExecutionToolOptions options, string contentRootPath, ILogger logger)
    {
        _options = options;
        _logger = logger;

        var configuredRoot = options.RootDirectory ?? string.Empty;
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? string.Empty
            : (Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(contentRootPath, configuredRoot));

        _rootFullPath = string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : Path.GetFullPath(root);
    }

    public bool TryResolveWorkingDirectory(string? userRelativeDir, out string workingDir, out string? error)
    {
        error = null;
        workingDir = string.Empty;

        if (!_options.Enabled)
        {
            error = "Code execution tools are disabled (CodeExecution:Enabled=false)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_rootFullPath))
        {
            error = "Code execution tool misconfigured: CodeExecution:RootDirectory is not set";
            return false;
        }

        if (!Directory.Exists(_rootFullPath))
        {
            error = "Code execution tool misconfigured: CodeExecution:RootDirectory does not exist";
            return false;
        }

        var combined = string.IsNullOrWhiteSpace(userRelativeDir)
            ? _rootFullPath
            : Path.Combine(_rootFullPath, userRelativeDir);

        string full;
        try
        {
            full = Path.GetFullPath(combined);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Invalid working_dir: {WorkingDir}", userRelativeDir);
            error = "Invalid working_dir";
            return false;
        }

        if (!IsWithinRoot(full))
        {
            error = "working_dir escapes sandbox root";
            return false;
        }

        if (!Directory.Exists(full))
        {
            error = "working_dir does not exist";
            return false;
        }

        workingDir = full;
        return true;
    }

    private bool IsWithinRoot(string fullPath)
    {
        var root = _rootFullPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(fullPath, root, comparison))
        {
            return true;
        }

        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSep, comparison);
    }
}
