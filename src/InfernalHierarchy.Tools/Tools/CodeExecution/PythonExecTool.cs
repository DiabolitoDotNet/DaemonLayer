using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.CodeExecution;

public sealed class PythonExecTool : ITool
{
    private readonly CodeExecutionToolOptions _options;
    private readonly CodeExecutionSandbox _sandbox;
    private readonly IProcessRunner _runner;
    private readonly ILogger<PythonExecTool> _logger;

    public PythonExecTool(
        IOptions<CodeExecutionToolOptions> options,
        IHostEnvironment env,
        IProcessRunner runner,
        ILogger<PythonExecTool> logger)
    {
        _options = options.Value;
        _sandbox = new CodeExecutionSandbox(_options, env.ContentRootPath, logger);
        _runner = runner;
        _logger = logger;
    }

    public string Name => "python_exec";

    public string Description => "Execute Python code (allowlisted, sandboxed working directory). Params: code (required), args (optional), working_dir (optional), timeout_ms (optional), max_output_bytes (optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("Code execution is disabled (CodeExecution:Enabled=false)");
        }

        if (!_options.EnablePython)
        {
            return Fail("Python execution is disabled (CodeExecution:EnablePython=false)");
        }

        var code = GetString(parameters, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            return Fail("Missing required parameter: code");
        }

        if (code.Length > _options.MaxCodeChars)
        {
            return Fail($"Code is too long (length={code.Length}, limit={_options.MaxCodeChars})");
        }

        var workingDirParam = GetString(parameters, "working_dir");
        if (!_sandbox.TryResolveWorkingDirectory(workingDirParam, out var workingDir, out var sandboxError))
        {
            return Fail(sandboxError ?? "Invalid working_dir");
        }

        var timeoutMs = GetInt(parameters, "timeout_ms") ?? _options.TimeoutMs;
        if (timeoutMs <= 0 || timeoutMs > _options.TimeoutMs)
        {
            timeoutMs = _options.TimeoutMs;
        }

        var maxOutputBytes = GetInt(parameters, "max_output_bytes") ?? _options.MaxOutputBytes;
        if (maxOutputBytes <= 0 || maxOutputBytes > _options.MaxOutputBytes)
        {
            maxOutputBytes = _options.MaxOutputBytes;
        }

        var args = GetStringList(parameters, "args");

        var argv = new List<string>
        {
            "-I",
            "-c",
            code
        };

        if (args.Count > 0)
        {
            argv.Add("--");
            argv.AddRange(args);
        }

        var request = new ProcessRunRequest(
            FileName: _options.PythonExecutable,
            Arguments: argv,
            WorkingDirectory: workingDir,
            TimeoutMs: timeoutMs,
            MaxOutputBytes: maxOutputBytes,
            EnvironmentVariables: new Dictionary<string, string>
            {
                ["PYTHONIOENCODING"] = "utf-8"
            });

        ProcessRunResult result;
        try
        {
            result = await _runner.RunAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }

        var output = JoinOutput(result.StdOut, result.StdErr);

        _logger.LogInformation(
            "🐍 python_exec exit={ExitCode} timeout={TimedOut} truncated={Truncated} duration_ms={DurationMs}",
            result.ExitCode,
            result.TimedOut,
            result.Truncated,
            (long)result.Duration.TotalMilliseconds);

        if (result.TimedOut)
        {
            return new ToolResult
            {
                Success = false,
                Output = output,
                Error = "Process timed out",
                Metadata = new Dictionary<string, object>
                {
                    ["exit_code"] = result.ExitCode,
                    ["timed_out"] = true,
                    ["truncated"] = result.Truncated,
                    ["duration_ms"] = (long)result.Duration.TotalMilliseconds
                }
            };
        }

        var success = result.ExitCode == 0;

        return new ToolResult
        {
            Success = success,
            Output = output,
            Error = success ? null : $"Process exited with code {result.ExitCode}",
            Metadata = new Dictionary<string, object>
            {
                ["exit_code"] = result.ExitCode,
                ["timed_out"] = false,
                ["truncated"] = result.Truncated,
                ["duration_ms"] = (long)result.Duration.TotalMilliseconds
            }
        };
    }

    private static string JoinOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        return $"{stdout}\n\n[stderr]\n{stderr}";
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

        if (value is int i) return i;
        if (value is long l) return l is < int.MinValue or > int.MaxValue ? null : (int)l;

        if (value is string s && int.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static List<string> GetStringList(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return new List<string>();
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in je.EnumerateArray())
                {
                    var s = item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = je.GetString();
                return string.IsNullOrWhiteSpace(s) ? new List<string>() : new List<string> { s };
            }
        }

        var asString = value.ToString();
        if (string.IsNullOrWhiteSpace(asString))
        {
            return new List<string>();
        }

        // Allow comma-separated fallback.
        return asString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }
}
