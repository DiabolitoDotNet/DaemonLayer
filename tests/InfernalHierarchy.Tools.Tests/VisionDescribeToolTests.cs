using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Vision;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class VisionDescribeToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenEnabledAndPathIsValid_ReturnsVisionOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-vision-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var imagePath = Path.Combine(root, "sample.png");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var options = Microsoft.Extensions.Options.Options.Create(new VisionToolOptions
        {
            Enabled = true,
            RootDirectory = root,
            TimeoutMs = 10_000,
            MaxInputBytes = 1024 * 1024,
            MaxOutputChars = 5000,
            DefaultPrompt = "test prompt"
        });

        var tool = new VisionDescribeTool(
            options,
            new FakeImageLlmClient("vision response"),
            Mock.Of<ILogger<VisionDescribeTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["path"] = imagePath,
            ["question"] = "What is this image?"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("vision response");
        result.Metadata["mime_type"].Should().Be("image/png");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPathOutsideRoot_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-vision-tests", Guid.NewGuid().ToString("n"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "infernal-vision-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);

        var imagePath = Path.Combine(outsideRoot, "sample.png");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var options = Microsoft.Extensions.Options.Options.Create(new VisionToolOptions
        {
            Enabled = true,
            RootDirectory = root
        });

        var tool = new VisionDescribeTool(
            options,
            new FakeImageLlmClient("vision response"),
            Mock.Of<ILogger<VisionDescribeTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["path"] = imagePath
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside the configured RootDirectory");
    }

    private sealed class FakeImageLlmClient : ILlmClient, IImageLlmClient
    {
        private readonly string _response;

        public FakeImageLlmClient(string response)
        {
            _response = response;
        }

        public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => Task.FromResult(_response);

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(_response);

        public Task<string> GetImageCompletionAsync(
            string systemPrompt,
            string userMessage,
            byte[] imageBytes,
            string mimeType,
            string? modelOverride = null,
            CancellationToken ct = default)
            => Task.FromResult(_response);
    }
}
