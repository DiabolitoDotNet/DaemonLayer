using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.FileSystem;

public sealed class FileReadTool : ITool
{
    private readonly FileSystemToolOptions _options;
    private readonly FileSystemSandbox _sandbox;

    public FileReadTool(
        IOptions<FileSystemToolOptions> options,
        IHostEnvironment env,
        ILogger<FileReadTool> logger)
    {
        _options = options.Value;
        _sandbox = new FileSystemSandbox(_options, env.ContentRootPath, logger);
    }

    public string Name => "fs_read";

    public string Description => "Read a local file from the sandboxed root. Params: path (optional: max_bytes).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var path = GetString(parameters, "path");

        if (!_sandbox.TryResolvePath(path, out var fullPath, out var error))
        {
            return Fail(error ?? "Invalid path");
        }

        var maxBytes = GetInt(parameters, "max_bytes") ?? _options.MaxReadBytes;
        if (maxBytes <= 0)
        {
            return Fail("max_bytes must be > 0");
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch
        {
            return Fail("Invalid file path");
        }

        if (!fileInfo.Exists)
        {
            return Fail("File not found");
        }

        if (fileInfo.Length > maxBytes)
        {
            return Fail($"File is too large to read (size={fileInfo.Length} bytes, limit={maxBytes} bytes)");
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            return new ToolResult
            {
                Success = true,
                Output = content,
                Metadata = new Dictionary<string, object>
                {
                    ["path"] = path ?? string.Empty,
                    ["size_bytes"] = fileInfo.Length
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
