using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Workflow;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace InfernalHierarchy.Tools.Tests;

public sealed class RepoAnalyzeToolTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsFailure()
    {
        var env = new FakeHostEnvironment();
        var tool = new RepoAnalyzeTool(
            MsOptions.Create(new DeliveryWorkflowOptions { Enabled = false }),
            env);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDotnetRepo_ProducesDotnetRecommendations()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-repo-analysis", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "sample.sln"), "Microsoft Visual Studio Solution File");

        var env = new FakeHostEnvironment { ContentRootPath = root };
        var tool = new RepoAnalyzeTool(
            MsOptions.Create(new DeliveryWorkflowOptions
            {
                Enabled = true,
                RootDirectory = root,
                MaxDiscoveryFiles = 200
            }),
            env);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeTrue();

        using var doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("capabilities").GetProperty("dotnet").GetBoolean().Should().BeTrue();

        var steps = doc.RootElement.GetProperty("recommended_steps").EnumerateArray().ToList();
        steps.Should().NotBeEmpty();
        steps.Any(x => x.GetProperty("stage").GetString() == "build").Should().BeTrue();
    }
}