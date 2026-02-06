using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.FileSystem;

public sealed class FileWriteTool : ITool
{
    private readonly FileSystemToolOptions _options;
    private readonly FileSystemSandbox _sandbox;
    private readonly ILogger<FileWriteTool> _logger;

    public FileWriteTool(
        IOptions<FileSystemToolOptions> options,
        IHostEnvironment env,
        ILogger<FileWriteTool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _sandbox = new FileSystemSandbox(_options, env.ContentRootPath, logger);
    }

    public string Name => "fs_write";

    public string Description => "Write a local file within the sandboxed root. Params: path, content (optional: overwrite).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("File system tools are disabled (FileSystem:Enabled=false)");
        }

        if (!_options.AllowWrite)
        {
            return Fail("File writing is disabled (FileSystem:AllowWrite=false)");
        }

        var path = GetString(parameters, "path");
        var content = GetString(parameters, "content") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Missing required parameter: path");
        }

        if (!_sandbox.TryResolvePath(path, out var fullPath, out var pathError))
        {
            return Fail(pathError ?? "Invalid path");
        }

        var overwrite = GetBool(parameters, "overwrite") ?? false;

        if (content.Length > _options.MaxWriteBytes)
        {
            return Fail($"Content is too large to write (size={content.Length} chars, limit={_options.MaxWriteBytes})");
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(fullPath) && !overwrite)
            {
                return Fail("File already exists (set overwrite=true to replace)");
            }

            await File.WriteAllTextAsync(fullPath, content, ct).ConfigureAwait(false);

            _logger.LogInformation("📝 Wrote file {Path}", fullPath);

            return new ToolResult
            {
                Success = true,
                Output = "File written",
                Metadata = new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["bytes"] = content.Length,
                    ["overwrite"] = overwrite
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
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

    private static bool? GetBool(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is string s && bool.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
