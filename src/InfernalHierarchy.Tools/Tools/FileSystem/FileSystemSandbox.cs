
namespace InfernalHierarchy.Tools.Tools.FileSystem;

internal sealed class FileSystemSandbox
{
    private readonly FileSystemToolOptions _options;
    private readonly ILogger _logger;
    private readonly string _rootFullPath;

    public string RootFullPath => _rootFullPath;

    public FileSystemSandbox(FileSystemToolOptions options, string contentRootPath, ILogger logger)
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

    public bool TryResolvePath(string? userPath, out string resolvedFullPath, out string? error)
    {
        error = null;
        resolvedFullPath = string.Empty;

        if (!_options.Enabled)
        {
            error = "File system tools are disabled (FileSystem:Enabled=false)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_rootFullPath))
        {
            error = "File system tool misconfigured: FileSystem:RootDirectory is not set";
            return false;
        }

        if (string.IsNullOrWhiteSpace(userPath))
        {
            error = "Missing required parameter: path";
            return false;
        }

        var combined = Path.IsPathRooted(userPath)
            ? userPath
            : Path.Combine(_rootFullPath, userPath);

        string full;
        try
        {
            full = Path.GetFullPath(combined);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Invalid path: {Path}", userPath);
            error = "Invalid path";
            return false;
        }

        if (!IsWithinRoot(full))
        {
            error = "Path escapes sandbox root";
            return false;
        }

        var ext = Path.GetExtension(full);
        if (!IsExtensionAllowed(ext))
        {
            error = $"File extension '{ext}' is not allowed";
            return false;
        }

        resolvedFullPath = full;
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

    private bool IsExtensionAllowed(string? ext)
    {
        if (_options.AllowedExtensions is null || _options.AllowedExtensions.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(ext))
        {
            return false;
        }

        return _options.AllowedExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
    }
}
