using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.CodeExecution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace InfernalHierarchy.Tools.Tests;

public sealed class CodeExecutionToolsTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class CapturingRunner : IProcessRunner
    {
        public ProcessRunRequest? LastRequest { get; private set; }
        public ProcessRunResult NextResult { get; set; } = new(
            ExitCode: 0,
            TimedOut: false,
            StdOut: "ok",
            StdErr: string.Empty,
            Truncated: false,
            Duration: TimeSpan.FromMilliseconds(5));

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    [Fact]
    public async Task PythonExec_WhenGloballyDisabled_ReturnsFailure_AndDoesNotInvokeRunner()
    {
        var env = new FakeHostEnvironment();
        var runner = new CapturingRunner();

        var tool = new PythonExecTool(
            MsOptions.Create(new CodeExecutionToolOptions { Enabled = false, EnablePython = true }),
            env,
            runner,
            NullLogger<PythonExecTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["code"] = "print('hi')" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        runner.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task PythonExec_BuildsExpectedProcessRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var runner = new CapturingRunner();

        var opts = new CodeExecutionToolOptions
        {
            Enabled = true,
            RootDirectory = root,
            EnablePython = true,
            PythonExecutable = "python",
            TimeoutMs = 10_000,
            MaxOutputBytes = 100_000,
            MaxCodeChars = 5000
        };

        var tool = new PythonExecTool(MsOptions.Create(opts), env, runner, NullLogger<PythonExecTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["code"] = "print('ok')",
            ["args"] = new List<string> { "a", "b" },
            ["working_dir"] = "sub",
            ["timeout_ms"] = 1234,
            ["max_output_bytes"] = 4321
        });

        result.Success.Should().BeTrue();
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.FileName.Should().Be("python");
        runner.LastRequest.WorkingDirectory.Should().Be(Path.Combine(root, "sub"));
        runner.LastRequest.TimeoutMs.Should().Be(1234);
        runner.LastRequest.MaxOutputBytes.Should().Be(4321);

        runner.LastRequest.Arguments.Should().ContainInOrder("-I", "-c", "print('ok')");
        runner.LastRequest.Arguments.Should().Contain("--");
        runner.LastRequest.Arguments.Should().Contain("a");
        runner.LastRequest.Arguments.Should().Contain("b");
    }

    [Fact]
    public async Task NodeExec_BuildsExpectedProcessRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var runner = new CapturingRunner();

        var opts = new CodeExecutionToolOptions
        {
            Enabled = true,
            RootDirectory = root,
            EnableNode = true,
            NodeExecutable = "node",
            TimeoutMs = 10_000,
            MaxOutputBytes = 100_000,
            MaxCodeChars = 5000
        };

        var tool = new NodeExecTool(MsOptions.Create(opts), env, runner, NullLogger<NodeExecTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["code"] = "console.log('ok')",
            ["args"] = "x,y"
        });

        result.Success.Should().BeTrue();
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.FileName.Should().Be("node");
        runner.LastRequest.WorkingDirectory.Should().Be(root);
        runner.LastRequest.Arguments.Should().ContainInOrder("-e", "console.log('ok')");
        runner.LastRequest.Arguments.Should().Contain("--");
        runner.LastRequest.Arguments.Should().Contain("x");
        runner.LastRequest.Arguments.Should().Contain("y");
    }

    [Fact]
    public async Task PythonExec_WhenNonZeroExit_ReturnsFailure_WithExitCodeMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var runner = new CapturingRunner
        {
            NextResult = new ProcessRunResult(
                ExitCode: 2,
                TimedOut: false,
                StdOut: string.Empty,
                StdErr: "boom",
                Truncated: false,
                Duration: TimeSpan.FromMilliseconds(5))
        };

        var opts = new CodeExecutionToolOptions
        {
            Enabled = true,
            RootDirectory = root,
            EnablePython = true,
            PythonExecutable = "python",
        };

        var tool = new PythonExecTool(MsOptions.Create(opts), env, runner, NullLogger<PythonExecTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["code"] = "raise Exception('x')" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("code 2");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey("exit_code");
        result.Metadata["exit_code"].Should().Be(2);
    }
}
