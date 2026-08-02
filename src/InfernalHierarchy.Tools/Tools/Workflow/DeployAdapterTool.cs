using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.Workflow;

public sealed class DeployAdapterTool : ITool
{
    private readonly DeliveryWorkflowOptions _options;
    private readonly IHostEnvironment _env;
    private readonly IProcessRunner _runner;

    public DeployAdapterTool(
        IOptions<DeliveryWorkflowOptions> options,
        IHostEnvironment env,
        IProcessRunner runner)
    {
        _options = options.Value;
        _env = env;
        _runner = runner;
    }

    public string Name => "deploy_adapter";

    public string Description => "Run controlled deploy adapters with rollback hooks. Params: adapter, environment, action(deploy|rollback), artifact(optional), rollback_on_failure(optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("Delivery workflow tools are disabled (DeliveryWorkflow:Enabled=false)");
        }

        var adapterId = GetString(parameters, "adapter");
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            return Fail("Missing required parameter: adapter");
        }

        if (!_options.Adapters.TryGetValue(adapterId, out var adapter) || !adapter.Enabled)
        {
            return Fail($"Unknown or disabled deploy adapter '{adapterId}'");
        }

        var environment = GetString(parameters, "environment");
        if (string.IsNullOrWhiteSpace(environment))
        {
            return Fail("Missing required parameter: environment");
        }

        if (adapter.AllowedEnvironments.Count > 0
            && !adapter.AllowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase))
        {
            return Fail($"Environment '{environment}' is not allowed for adapter '{adapterId}'");
        }

        var action = (GetString(parameters, "action") ?? "deploy").Trim();
        if (!action.Equals("deploy", StringComparison.OrdinalIgnoreCase)
            && !action.Equals("rollback", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Invalid action. Allowed: deploy, rollback");
        }

        var artifact = GetString(parameters, "artifact") ?? string.Empty;
        var rollbackOnFailure = GetBool(parameters, "rollback_on_failure") ?? true;
        var timeoutMs = GetInt(parameters, "timeout_ms") ?? _options.DefaultTimeoutMs;

        var workingDirectory = ResolveWorkingDirectory(adapter.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return Fail("Deploy adapter working directory is invalid");
        }

        if (action.Equals("rollback", StringComparison.OrdinalIgnoreCase))
        {
            return await RunRollbackOnlyAsync(adapterId, adapter, environment, artifact, workingDirectory, timeoutMs, ct).ConfigureAwait(false);
        }

        return await RunDeployWithRollbackHookAsync(adapterId, adapter, environment, artifact, workingDirectory, timeoutMs, rollbackOnFailure, ct)
            .ConfigureAwait(false);
    }

    private async Task<ToolResult> RunDeployWithRollbackHookAsync(
        string adapterId,
        DeliveryAdapterOptions adapter,
        string environment,
        string artifact,
        string workingDirectory,
        int timeoutMs,
        bool rollbackOnFailure,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adapter.DeployExecutable))
        {
            return Fail($"Deploy adapter '{adapterId}' has no deploy executable configured");
        }

        var deployArgs = MaterializeArguments(adapter.DeployArguments, environment, artifact);
        var deployRun = await _runner.RunAsync(new ProcessRunRequest(
            FileName: adapter.DeployExecutable,
            Arguments: deployArgs,
            WorkingDirectory: workingDirectory,
            TimeoutMs: timeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        var deploySuccess = !deployRun.TimedOut && deployRun.ExitCode == 0;
        if (deploySuccess)
        {
            return new ToolResult
            {
                Success = true,
                Output = JoinOutput(deployRun.StdOut, deployRun.StdErr),
                Metadata = new Dictionary<string, object>
                {
                    ["adapter"] = adapterId,
                    ["action"] = "deploy",
                    ["environment"] = environment,
                    ["exit_code"] = deployRun.ExitCode,
                    ["duration_ms"] = (long)deployRun.Duration.TotalMilliseconds,
                    ["rollback_attempted"] = false
                }
            };
        }

        var deployOutput = JoinOutput(deployRun.StdOut, deployRun.StdErr);
        if (!rollbackOnFailure || string.IsNullOrWhiteSpace(adapter.RollbackExecutable))
        {
            return new ToolResult
            {
                Success = false,
                Output = deployOutput,
                Error = deployRun.TimedOut ? "Deploy timed out" : $"Deploy failed with exit code {deployRun.ExitCode}",
                Metadata = new Dictionary<string, object>
                {
                    ["adapter"] = adapterId,
                    ["action"] = "deploy",
                    ["environment"] = environment,
                    ["exit_code"] = deployRun.ExitCode,
                    ["rollback_attempted"] = false
                }
            };
        }

        var rollbackArgs = MaterializeArguments(adapter.RollbackArguments, environment, artifact);
        var rollbackRun = await _runner.RunAsync(new ProcessRunRequest(
            FileName: adapter.RollbackExecutable,
            Arguments: rollbackArgs,
            WorkingDirectory: workingDirectory,
            TimeoutMs: timeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        var rollbackSuccess = !rollbackRun.TimedOut && rollbackRun.ExitCode == 0;
        var output = $"[deploy]\n{deployOutput}\n\n[rollback]\n{JoinOutput(rollbackRun.StdOut, rollbackRun.StdErr)}";

        return new ToolResult
        {
            Success = false,
            Output = output,
            Error = rollbackSuccess
                ? $"Deploy failed and rollback completed (deploy exit code {deployRun.ExitCode})"
                : $"Deploy failed and rollback failed (deploy exit {deployRun.ExitCode}, rollback exit {rollbackRun.ExitCode})",
            Metadata = new Dictionary<string, object>
            {
                ["adapter"] = adapterId,
                ["action"] = "deploy",
                ["environment"] = environment,
                ["exit_code"] = deployRun.ExitCode,
                ["rollback_attempted"] = true,
                ["rollback_success"] = rollbackSuccess,
                ["rollback_exit_code"] = rollbackRun.ExitCode
            }
        };
    }

    private async Task<ToolResult> RunRollbackOnlyAsync(
        string adapterId,
        DeliveryAdapterOptions adapter,
        string environment,
        string artifact,
        string workingDirectory,
        int timeoutMs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adapter.RollbackExecutable))
        {
            return Fail($"Deploy adapter '{adapterId}' has no rollback executable configured");
        }

        var rollbackArgs = MaterializeArguments(adapter.RollbackArguments, environment, artifact);
        var rollbackRun = await _runner.RunAsync(new ProcessRunRequest(
            FileName: adapter.RollbackExecutable,
            Arguments: rollbackArgs,
            WorkingDirectory: workingDirectory,
            TimeoutMs: timeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        var success = !rollbackRun.TimedOut && rollbackRun.ExitCode == 0;

        return new ToolResult
        {
            Success = success,
            Output = JoinOutput(rollbackRun.StdOut, rollbackRun.StdErr),
            Error = success ? null : (rollbackRun.TimedOut ? "Rollback timed out" : $"Rollback failed with exit code {rollbackRun.ExitCode}"),
            Metadata = new Dictionary<string, object>
            {
                ["adapter"] = adapterId,
                ["action"] = "rollback",
                ["environment"] = environment,
                ["exit_code"] = rollbackRun.ExitCode
            }
        };
    }

    private string ResolveWorkingDirectory(string configured)
    {
        var baseRoot = Path.IsPathRooted(_options.RootDirectory)
            ? _options.RootDirectory
            : Path.Combine(_env.ContentRootPath, _options.RootDirectory);

        var value = string.IsNullOrWhiteSpace(configured) ? "." : configured;
        var target = Path.IsPathRooted(value) ? value : Path.Combine(baseRoot, value);
        return Path.GetFullPath(target);
    }

    private static List<string> MaterializeArguments(IEnumerable<string> args, string environment, string artifact)
    {
        return args
            .Select(x => x
                .Replace("{environment}", environment, StringComparison.OrdinalIgnoreCase)
                .Replace("{artifact}", artifact, StringComparison.OrdinalIgnoreCase))
            .ToList();
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