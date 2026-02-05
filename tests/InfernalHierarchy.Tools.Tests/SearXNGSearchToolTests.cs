using FluentAssertions;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class SearXNGSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenDisabled()
    {
        var options = Options.Create(new SearXNGOptions { Enabled = false });
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<SearXNGSearchTool>>();
        var tool = new SearXNGSearchTool(httpClient, options, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "x" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenQueryMissing()
    {
        var options = Options.Create(new SearXNGOptions { Enabled = true });
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<SearXNGSearchTool>>();
        var tool = new SearXNGSearchTool(httpClient, options, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("query");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnNoResults_WhenResponseEmpty()
    {
        var mockResponse = new { results = Array.Empty<object>() };

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(mockResponse))
            });

        using var httpClient = new HttpClient(mockHttpMessageHandler.Object, disposeHandler: false);
        var options = Options.Create(new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:1234") });
        var logger = Mock.Of<ILogger<SearXNGSearchTool>>();
        var tool = new SearXNGSearchTool(httpClient, options, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("No results");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFormattedResults_WhenResponseContainsItems()
    {
        var mockResponse = new
        {
            results = new[]
            {
                new { title = "T1", url = "https://example.com/1", content = "C1" },
                new { title = "T2", url = "https://example.com/2", content = "C2" }
            }
        };

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(mockResponse))
            });

        using var httpClient = new HttpClient(mockHttpMessageHandler.Object, disposeHandler: false);
        var options = Options.Create(new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:1234") });
        var logger = Mock.Of<ILogger<SearXNGSearchTool>>();
        var tool = new SearXNGSearchTool(httpClient, options, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Title: T1");
        result.Output.Should().Contain("URL: https://example.com/1");
        result.Output.Should().Contain("Snippet: C1");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["query"].Should().Be("test");
        result.Metadata["result_count"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenHttpFails()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        using var httpClient = new HttpClient(mockHttpMessageHandler.Object, disposeHandler: false);
        var options = Options.Create(new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:1234") });
        var logger = Mock.Of<ILogger<SearXNGSearchTool>>();
        var tool = new SearXNGSearchTool(httpClient, options, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Search failed");
    }
}
