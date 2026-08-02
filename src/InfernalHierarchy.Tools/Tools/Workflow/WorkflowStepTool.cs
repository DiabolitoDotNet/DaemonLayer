using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.Workflow;

public sealed class WorkflowStepTool : ITool
{
    private static readonly HashSet<string> ValidStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "install",
        "build",
        "test",
        "lint",
        "package"
    };

    private readonly DeliveryWorkflowOptions _options;
    private readonly IHostEnvironment _env;
    private readonly IProcessRunner _runner;

    public WorkflowStepTool(
        IOptions<DeliveryWorkflowOptions> options,
        IHostEnvironment env,
        IProcessRunner runner)
    {
        _options = options.Value;
        _env = env;
        _runner = runner;
    }

    public string Name => "workflow_step";

    public string Description => "Run workflow primitives for install/build/test/lint/package. Params: stage, repo_path(optional), command(optional), args(optional), timeout_ms(optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var stage = GetString(parameters, "stage");
        if (string.IsNullOrWhiteSpace(stage) || !ValidStages.Contains(stage))
        {
            return Fail("Missing or invalid stage. Allowed: install, build, test, lint, package");
        }

        var repoPath = GetString(parameters, "repo_path");
        if (!DeliveryWorkflowProbe.TryResolveRootDirectory(_options, _env, repoPath, out var root, out var error))
        {
            return Fail(error ?? "Unable to resolve repository path");
        }

        var probe = DeliveryWorkflowProbe.AnalyzeRepository(root, _options.MaxDiscoveryFiles);
        var executable = GetString(parameters, "command");
        var args = GetStringList(parameters, "args");

        if (string.IsNullOrWhiteSpace(executable))
        {
            (executable, args) = ResolveDefaultCommand(stage, probe, _options.PackageOutputDirectory);
            if (string.IsNullOrWhiteSpace(executable))
            {
                return Fail($"No default command available for stage '{stage}' in this repository");
            }
        }

        var timeoutMs = GetInt(parameters, "timeout_ms") ?? _options.DefaultTimeoutMs;
        timeoutMs = Math.Clamp(timeoutMs, 1_000, Math.Max(1_000, _options.DefaultTimeoutMs));

        var result = await _runner.RunAsync(new ProcessRunRequest(
            FileName: executable,
            Arguments: args,
            WorkingDirectory: root,
            TimeoutMs: timeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        var output = JoinOutput(result.StdOut, result.StdErr);
        var success = !result.TimedOut && result.ExitCode == 0;

        return new ToolResult
        {
            Success = success,
            Output = output,
            Error = success ? null : (result.TimedOut ? "Workflow step timed out" : $"Workflow step failed with exit code {result.ExitCode}"),
            Metadata = new Dictionary<string, object>
            {
                ["stage"] = stage,
                ["command"] = executable,
                ["arguments"] = string.Join(" ", args),
                ["repo_root"] = root,
                ["exit_code"] = result.ExitCode,
                ["timed_out"] = result.TimedOut,
                ["truncated"] = result.Truncated,
                ["duration_ms"] = (long)result.Duration.TotalMilliseconds
            }
        };
    }

    private static (string Executable, List<string> Args) ResolveDefaultCommand(
        string stage,
        DeliveryWorkflowProbe.ProbeResult probe,
        string packageOutputDirectory)
    {
        if (probe.HasDotnet)
        {
            return stage.ToLowerInvariant() switch
            {
                "install" => ("dotnet", new List<string> { "restore" }),
                "build" => ("dotnet", new List<string> { "build", "-c", "Release" }),
                "test" => ("dotnet", new List<string> { "test", "-c", "Release", "--no-build" }),
                "lint" => ("dotnet", new List<string> { "format", "--verify-no-changes" }),
                "package" => ("dotnet", new List<string> { "pack", "-c", "Release", "-o", packageOutputDirectory }),
                _ => (string.Empty, new List<string>())
            };
        }

        if (probe.HasNode)
        {
            return stage.ToLowerInvariant() switch
            {
                "install" => ("npm", new List<string> { "ci" }),
                "build" => ("npm", new List<string> { "run", "build" }),
                "test" => ("npm", new List<string> { "test" }),
                "lint" => ("npm", new List<string> { "run", "lint" }),
                "package" => ("npm", new List<string> { "pack" }),
                _ => (string.Empty, new List<string>())
            };
        }

        if (probe.HasPython)
        {
            return stage.ToLowerInvariant() switch
            {
                "install" => ("python", new List<string> { "-m", "pip", "install", "-r", "requirements.txt" }),
                "test" => ("python", new List<string> { "-m", "pytest" }),
                _ => (string.Empty, new List<string>())
            };
        }

        return (string.Empty, new List<string>());
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

        return value.ToString();
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
                    var text = item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text);
                    }
                }

                return list;
            }
        }

        var asText = value.ToString();
        if (string.IsNullOrWhiteSpace(asText))
        {
            return new List<string>();
        }

        return asText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }
}