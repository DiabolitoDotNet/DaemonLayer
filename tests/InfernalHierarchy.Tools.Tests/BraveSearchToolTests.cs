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

public class BraveSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenDisabled()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = false });
        var client = Mock.Of<IBraveSearchClient>();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "test query"
        };

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenApiKeyMissing()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "" });
        var client = Mock.Of<IBraveSearchClient>();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "test query"
        };

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenQueryMissing()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = Mock.Of<IBraveSearchClient>();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>();

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("query");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenQueryNotString()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = Mock.Of<IBraveSearchClient>();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = 123
        };

        var result = await tool.ExecuteAsync(parameters);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("query");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResults_WhenApiReturnsData()
    {
        // Arrange
        var mockResponse = new
        {
            web = new
            {
                results = new[]
                {
                    new { title = "Test Result 1", url = "https://example.com/1", description = "First result", age = (string?)"1d", page_age = (string?)"2026-02-04" },
                    new { title = "Test Result 2", url = "https://example.com/2", description = "Second result", age = (string?)null, page_age = (string?)null }
                }
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
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = new BraveSearchClient(httpClient, options, Mock.Of<ILogger<BraveSearchClient>>());
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "test query"
        };

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Test Result 1");
        result.Output.Should().Contain("https://example.com/1");
        result.Output.Should().Contain("First result");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnNoResults_WhenApiReturnsEmptyArray()
    {
        var mockResponse = new
        {
            web = new
            {
                results = Array.Empty<object>()
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
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = new BraveSearchClient(httpClient, options, Mock.Of<ILogger<BraveSearchClient>>());
        var tool = new BraveSearchTool(client, options, Mock.Of<ILogger<BraveSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("No results found.");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleApiErrors()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("Invalid API key")
            });

        using var httpClient = new HttpClient(mockHttpMessageHandler.Object, disposeHandler: false);
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "invalid-key" });
        var client = new BraveSearchClient(httpClient, options, Mock.Of<ILogger<BraveSearchClient>>());
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "test query"
        };

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleHttpRequestException()
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
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = new BraveSearchClient(httpClient, options, Mock.Of<ILogger<BraveSearchClient>>());
        var tool = new BraveSearchTool(client, options, Mock.Of<ILogger<BraveSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Network error");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleInvalidJson()
    {
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
                Content = new StringContent("not-json")
            });

        using var httpClient = new HttpClient(mockHttpMessageHandler.Object, disposeHandler: false);
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var client = new BraveSearchClient(httpClient, options, Mock.Of<ILogger<BraveSearchClient>>());
        var tool = new BraveSearchTool(client, options, Mock.Of<ILogger<BraveSearchTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "test" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid response format");
    }

    [Fact]
    public void Name_ShouldBeBraveSearch()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions());
        var client = Mock.Of<IBraveSearchClient>();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(client, options, logger);

        // Assert
        tool.Name.Should().Be("brave_search");
    }
}
