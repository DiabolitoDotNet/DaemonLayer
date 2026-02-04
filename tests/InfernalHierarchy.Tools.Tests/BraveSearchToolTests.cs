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

public class BraveSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenDisabled()
    {
        // Arrange
        var options = Options.Create(new BraveSearchOptions { Enabled = false });
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

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
        var options = Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "" });
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

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
        var options = Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

        var parameters = new Dictionary<string, object>();

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
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
                    new { title = "Test Result 1", url = "https://example.com/1", description = "First result" },
                    new { title = "Test Result 2", url = "https://example.com/2", description = "Second result" }
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
        var options = Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "test-key" });
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

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
        var options = Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "invalid-key" });
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

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
    public void Name_ShouldBeBraveSearch()
    {
        // Arrange
        var options = Options.Create(new BraveSearchOptions());
        using var httpClient = new HttpClient();
        var logger = Mock.Of<ILogger<BraveSearchTool>>();
        var tool = new BraveSearchTool(httpClient, options, logger);

        // Assert
        tool.Name.Should().Be("brave_search");
    }
}
