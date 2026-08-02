using FluentAssertions;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Workflow;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace InfernalHierarchy.Tools.Tests;

public sealed class DeliveryWorkflowToolsTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class SequencedRunner : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results;

        public List<ProcessRunRequest> Calls { get; } = new();

        public SequencedRunner(IEnumerable<ProcessRunResult> results)
        {
            _results = new Queue<ProcessRunResult>(results);
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct)
        {
            Calls.Add(request);
            if (_results.Count == 0)
            {
                return Task.FromResult(new ProcessRunResult(0, false, "ok", string.Empty, false, TimeSpan.FromMilliseconds(1)));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    [Fact]
    public async Task WorkflowStep_UsesDotnetDefaults_WhenRepoLooksLikeDotnet()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-workflow", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "test.sln"), "Solution");

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var runner = new SequencedRunner(new[]
        {
            new ProcessRunResult(0, false, "build ok", string.Empty, false, TimeSpan.FromMilliseconds(30))
        });

        var tool = new WorkflowStepTool(
            MsOptions.Create(new DeliveryWorkflowOptions
            {
                Enabled = true,
                RootDirectory = root,
                DefaultTimeoutMs = 30_000,
                MaxOutputBytes = 100_000
            }),
            env,
            runner);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["stage"] = "build"
        });

        result.Success.Should().BeTrue();
        runner.Calls.Should().HaveCount(1);
        runner.Calls[0].FileName.Should().Be("dotnet");
        runner.Calls[0].Arguments.Should().ContainInOrder("build", "-c", "Release");
    }

    [Fact]
    public async Task DeployAdapter_WhenDeployFails_AttemptsRollbackAndReturnsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-deploy", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var runner = new SequencedRunner(new[]
        {
            new ProcessRunResult(2, false, string.Empty, "deploy failed", false, TimeSpan.FromMilliseconds(20)),
            new ProcessRunResult(0, false, "rollback ok", string.Empty, false, TimeSpan.FromMilliseconds(15))
        });

        var tool = new DeployAdapterTool(
            MsOptions.Create(new DeliveryWorkflowOptions
            {
                Enabled = true,
                RootDirectory = root,
                DefaultTimeoutMs = 30_000,
                MaxOutputBytes = 100_000,
                Adapters = new Dictionary<string, DeliveryAdapterOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["local"] = new DeliveryAdapterOptions
                    {
                        Enabled = true,
                        WorkingDirectory = ".",
                        AllowedEnvironments = new List<string> { "sandbox" },
                        DeployExecutable = "dotnet",
                        DeployArguments = new List<string> { "build" },
                        RollbackExecutable = "dotnet",
                        RollbackArguments = new List<string> { "clean" }
                    }
                }
            }),
            env,
            runner);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["adapter"] = "local",
            ["environment"] = "sandbox",
            ["action"] = "deploy",
            ["rollback_on_failure"] = true
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("rollback completed");
        runner.Calls.Should().HaveCount(2);
        result.Metadata.Should().ContainKey("rollback_attempted");
        result.Metadata["rollback_attempted"].Should().Be(true);
        result.Metadata["rollback_success"].Should().Be(true);
    }
}