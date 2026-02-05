using FluentAssertions;
using InfernalHierarchy.Tools.Clients.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class WebSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnSearXngResult_WhenSearXngSucceeds()
    {
        var searxResponse = new
        {
            results = new[]
            {
                new { title = "S1", url = "https://searx/1", content = "Snippet" }
            }
        };

        var searxHandler = new Mock<HttpMessageHandler>();
        searxHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(searxResponse))
            });

        var braveHandler = new Mock<HttpMessageHandler>();
        braveHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        using var searxClient = new HttpClient(searxHandler.Object, disposeHandler: false);
        using var braveClient = new HttpClient(braveHandler.Object, disposeHandler: false);

        var searxOptions = Microsoft.Extensions.Options.Options.Create(
            new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:8080") });
        var braveOptions = Microsoft.Extensions.Options.Options.Create(
            new BraveSearchOptions { Enabled = true, ApiKey = "test" });

        var searxTypedClient = new SearXngClient(searxClient, searxOptions, Mock.Of<ILogger<SearXngClient>>());
        var braveTypedClient = new BraveSearchClient(braveClient, braveOptions, Mock.Of<ILogger<BraveSearchClient>>());

        var searx = new SearXNGSearchTool(
            searxTypedClient,
            searxOptions,
            Mock.Of<ILogger<SearXNGSearchTool>>());

        var brave = new BraveSearchTool(
            braveTypedClient,
            braveOptions,
            Mock.Of<ILogger<BraveSearchTool>>());

        var tool = new WebSearchTool(searx, brave, Mock.Of<ILogger<WebSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "q" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("S1");

        braveHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToBrave_WhenSearXngFails()
    {
        var braveResponse = new
        {
            web = new
            {
                results = new[]
                {
                    new { title = "B1", url = "https://brave/1", description = "Desc" }
                }
            }
        };

        var searxHandler = new Mock<HttpMessageHandler>();
        searxHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));

        var braveHandler = new Mock<HttpMessageHandler>();
        braveHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(braveResponse))
            });

        using var searxClient = new HttpClient(searxHandler.Object, disposeHandler: false);
        using var braveClient = new HttpClient(braveHandler.Object, disposeHandler: false);

        var searxOptions = Microsoft.Extensions.Options.Options.Create(
            new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:8080") });
        var braveOptions = Microsoft.Extensions.Options.Options.Create(
            new BraveSearchOptions { Enabled = true, ApiKey = "test" });

        var searxTypedClient = new SearXngClient(searxClient, searxOptions, Mock.Of<ILogger<SearXngClient>>());
        var braveTypedClient = new BraveSearchClient(braveClient, braveOptions, Mock.Of<ILogger<BraveSearchClient>>());

        var searx = new SearXNGSearchTool(
            searxTypedClient,
            searxOptions,
            Mock.Of<ILogger<SearXNGSearchTool>>());

        var brave = new BraveSearchTool(
            braveTypedClient,
            braveOptions,
            Mock.Of<ILogger<BraveSearchTool>>());

        var tool = new WebSearchTool(searx, brave, Mock.Of<ILogger<WebSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "q" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("B1");

        braveHandler.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenAllProvidersFail()
    {
        var searxHandler = new Mock<HttpMessageHandler>();
        searxHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));

        var braveHandler = new Mock<HttpMessageHandler>();
        braveHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("no")
            });

        using var searxClient = new HttpClient(searxHandler.Object, disposeHandler: false);
        using var braveClient = new HttpClient(braveHandler.Object, disposeHandler: false);

        var searxOptions = Microsoft.Extensions.Options.Options.Create(
            new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:8080") });
        var braveOptions = Microsoft.Extensions.Options.Options.Create(
            new BraveSearchOptions { Enabled = true, ApiKey = "test" });

        var searxTypedClient = new SearXngClient(searxClient, searxOptions, Mock.Of<ILogger<SearXngClient>>());
        var braveTypedClient = new BraveSearchClient(braveClient, braveOptions, Mock.Of<ILogger<BraveSearchClient>>());

        var searx = new SearXNGSearchTool(
            searxTypedClient,
            searxOptions,
            Mock.Of<ILogger<SearXNGSearchTool>>());

        var brave = new BraveSearchTool(
            braveTypedClient,
            braveOptions,
            Mock.Of<ILogger<BraveSearchTool>>());

        var tool = new WebSearchTool(searx, brave, Mock.Of<ILogger<WebSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "q" });

        result.Success.Should().BeFalse();
    }
}
