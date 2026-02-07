using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.FileSystem;

public sealed class FileSearchTool : ITool
{
    private readonly FileSystemToolOptions _options;
    private readonly FileSystemSandbox _sandbox;
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(
        IOptions<FileSystemToolOptions> options,
        IHostEnvironment env,
        ILogger<FileSearchTool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _sandbox = new FileSystemSandbox(_options, env.ContentRootPath, logger);
    }

    public string Name => "fs_search";

    public string Description => "Search for text in files under the sandboxed root. Params: query (optional: glob, max_results).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("File system tools are disabled (FileSystem:Enabled=false)");
        }

        var query = GetString(parameters, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Fail("Missing required parameter: query");
        }

        var glob = GetString(parameters, "glob") ?? "*";
        var maxResults = GetInt(parameters, "max_results") ?? _options.MaxSearchResults;
        if (maxResults <= 0)
        {
            return Fail("max_results must be > 0");
        }

        if (maxResults > _options.MaxSearchResults)
        {
            maxResults = _options.MaxSearchResults;
        }

        var rootPath = _sandbox.RootFullPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return Fail("File system tool misconfigured: sandbox root is missing or does not exist");
        }

        var filesScanned = 0;
        var hits = new List<string>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootPath, glob, SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            return Fail($"File enumeration failed: {ex.Message}");
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            filesScanned++;
            if (filesScanned > _options.MaxSearchFilesScanned)
            {
                break;
            }

            if (!_sandbox.TryResolvePath(file, out var fullPath, out _))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(fullPath);
            }
            catch
            {
                continue;
            }

            if (!info.Exists)
            {
                continue;
            }

            if (info.Length > _options.MaxSearchFileBytes)
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (content.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hits.Add(RelativizePath(rootPath, fullPath));
                if (hits.Count >= maxResults)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("🔎 fs_search query='{Query}' hits={Hits} scanned={Scanned}", query, hits.Count, filesScanned);

        var output = hits.Count == 0
            ? "No matches"
            : string.Join("\n", hits.Select(x => $"- {x}"));

        return new ToolResult
        {
            Success = true,
            Output = output,
            Metadata = new Dictionary<string, object>
            {
                ["hits"] = hits.Count,
                ["files_scanned"] = filesScanned,
                ["max_results"] = maxResults
            }
        };
    }

    private static string RelativizePath(string root, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Output = string.Empty,
        Error = message
    };

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString()
        };
    }

    private static int? GetInt(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return l is < int.MinValue or > int.MaxValue ? null : (int)l;
        }

        if (value is string s && int.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
