using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Tools.Tools.Workflow;

public sealed class RepoAnalyzeTool : ITool
{
    private readonly DeliveryWorkflowOptions _options;
    private readonly IHostEnvironment _env;

    public RepoAnalyzeTool(IOptions<DeliveryWorkflowOptions> options, IHostEnvironment env)
    {
        _options = options.Value;
        _env = env;
    }

    public string Name => "repo_analyze";

    public string Description => "Analyze a repository and propose build/deploy workflow primitives. Params: repo_path (optional).";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var repoPath = GetString(parameters, "repo_path");
        if (!DeliveryWorkflowProbe.TryResolveRootDirectory(_options, _env, repoPath, out var root, out var error))
        {
            return Task.FromResult(Fail(error ?? "Unable to resolve repository path"));
        }

        var probe = DeliveryWorkflowProbe.AnalyzeRepository(root, _options.MaxDiscoveryFiles);

        var recommendations = new List<object>();

        if (probe.HasDotnet)
        {
            recommendations.Add(new { stage = "install", executable = "dotnet", args = new[] { "restore" } });
            recommendations.Add(new { stage = "build", executable = "dotnet", args = new[] { "build", "-c", "Release" } });
            recommendations.Add(new { stage = "test", executable = "dotnet", args = new[] { "test", "-c", "Release", "--no-build" } });
            recommendations.Add(new { stage = "package", executable = "dotnet", args = new[] { "pack", "-c", "Release", "-o", _options.PackageOutputDirectory } });
        }

        if (probe.HasNode)
        {
            recommendations.Add(new { stage = "install", executable = "npm", args = new[] { "ci" } });
            recommendations.Add(new { stage = "build", executable = "npm", args = new[] { "run", "build" } });
            recommendations.Add(new { stage = "test", executable = "npm", args = new[] { "test" } });
            recommendations.Add(new { stage = "lint", executable = "npm", args = new[] { "run", "lint" } });
        }

        if (probe.HasPython)
        {
            recommendations.Add(new { stage = "install", executable = "python", args = new[] { "-m", "pip", "install", "-r", "requirements.txt" } });
            recommendations.Add(new { stage = "test", executable = "python", args = new[] { "-m", "pytest" } });
        }

        var output = JsonSerializer.Serialize(new
        {
            root,
            files_scanned = probe.FilesScanned,
            capabilities = new
            {
                dotnet = probe.HasDotnet,
                node = probe.HasNode,
                python = probe.HasPython,
                docker = probe.HasDocker,
                git = probe.HasGit
            },
            detected_signals = probe.Signals,
            recommended_steps = recommendations
        });

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Output = output,
            Metadata = new Dictionary<string, object>
            {
                ["repo_root"] = root,
                ["files_scanned"] = probe.FilesScanned,
                ["recommended_steps"] = recommendations.Count
            }
        });
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
}