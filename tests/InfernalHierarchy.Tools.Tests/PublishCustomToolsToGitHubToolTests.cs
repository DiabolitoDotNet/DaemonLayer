using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Meta;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class PublishCustomToolsToGitHubToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldFailFast_AndNotTouchDependencies()
    {
        var store = new Mock<ICustomToolStore>(MockBehavior.Strict);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var tool = new PublishCustomToolsToGitHubTool(
            store.Object,
            http.Object,
            new TestOptionsMonitor<GitHubPublisherOptions>(new GitHubPublisherOptions { Enabled = false }),
            NullLogger<PublishCustomToolsToGitHubTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMissingToken_ShouldFailFast_AndNotReadStore()
    {
        var store = new Mock<ICustomToolStore>(MockBehavior.Strict);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var tool = new PublishCustomToolsToGitHubTool(
            store.Object,
            http.Object,
            new TestOptionsMonitor<GitHubPublisherOptions>(new GitHubPublisherOptions
            {
                Enabled = true,
                Owner = "me",
                Repository = "infernal-custom-tools",
                Token = ""
            }),
            NullLogger<PublishCustomToolsToGitHubTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Token");
    }

    private sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private readonly T _current;
        public TestOptionsMonitor(T current) => _current = current;
        public T CurrentValue => _current;
        public T Get(string? name) => _current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
