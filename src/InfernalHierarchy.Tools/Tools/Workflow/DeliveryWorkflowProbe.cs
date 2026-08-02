using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.Workflow;

internal static class DeliveryWorkflowProbe
{
    internal sealed record ProbeResult(
        bool HasDotnet,
        bool HasNode,
        bool HasPython,
        bool HasDocker,
        bool HasGit,
        int FilesScanned,
        IReadOnlyList<string> Signals);

    public static bool TryResolveRootDirectory(
        DeliveryWorkflowOptions options,
        IHostEnvironment env,
        string? requestedPath,
        out string fullPath,
        out string? error)
    {
        error = null;
        fullPath = string.Empty;

        if (!options.Enabled)
        {
            error = "Delivery workflow tools are disabled (DeliveryWorkflow:Enabled=false)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            error = "Delivery workflow root directory is not configured";
            return false;
        }

        var baseRoot = Path.IsPathRooted(options.RootDirectory)
            ? options.RootDirectory
            : Path.Combine(env.ContentRootPath, options.RootDirectory);

        var target = string.IsNullOrWhiteSpace(requestedPath)
            ? baseRoot
            : (Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(baseRoot, requestedPath));

        try
        {
            var normalized = Path.GetFullPath(target);
            var normalizedRoot = Path.GetFullPath(baseRoot);

            if (!normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "Requested path is outside delivery workflow root";
                return false;
            }

            if (!Directory.Exists(normalized))
            {
                error = "Requested repository path does not exist";
                return false;
            }

            fullPath = normalized;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static ProbeResult AnalyzeRepository(string rootPath, int maxFiles)
    {
        var scanned = 0;
        var hasDotnet = false;
        var hasNode = false;
        var hasPython = false;
        var hasDocker = false;
        var hasGit = false;

        var signals = new List<string>();

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            scanned++;
            if (scanned > Math.Max(100, maxFiles))
            {
                break;
            }

            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            if (!hasDotnet && (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)))
            {
                hasDotnet = true;
                signals.Add($"dotnet:{name}");
            }

            if (!hasNode && name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            {
                hasNode = true;
                signals.Add("node:package.json");
            }

            if (!hasPython && (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
                || name.Equals("setup.py", StringComparison.OrdinalIgnoreCase)))
            {
                hasPython = true;
                signals.Add($"python:{name}");
            }

            if (!hasDocker && name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
            {
                hasDocker = true;
                signals.Add("docker:Dockerfile");
            }

            if (!hasGit && name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
            {
                hasGit = true;
                signals.Add("scm:.gitignore");
            }
        }

        return new ProbeResult(hasDotnet, hasNode, hasPython, hasDocker, hasGit, scanned, signals);
    }
}