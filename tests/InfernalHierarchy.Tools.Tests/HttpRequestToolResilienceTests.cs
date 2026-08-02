using System.Net;
using System.Text;
using FluentAssertions;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class HttpRequestToolResilienceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTransientHttpFailure_ShouldRetryAndSucceed()
    {
        var calls = 0;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                calls++;
                if (calls == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("down", Encoding.UTF8, "text/plain")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok", Encoding.UTF8, "text/plain")
                };
            });

        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateClient(nameof(HttpRequestTool))).Returns(new HttpClient(handler.Object, disposeHandler: false));

        var options = Microsoft.Extensions.Options.Options.Create(new HttpRequestToolOptions
        {
            Enabled = true,
            AllowedHosts = new List<string> { "example.com" },
            AllowedMethods = new List<string> { "GET" },
            TimeoutMs = 10_000,
            MaxResponseBytes = 8_192
        });

        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var tool = new HttpRequestTool(factory.Object, options, NullLogger<HttpRequestTool>.Instance, exceptionHandler);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["url"] = "https://example.com/data",
            ["method"] = "GET"
        });

        result.Success.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPermanentHttpFailure_ShouldNotRetry()
    {
        var calls = 0;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("bad", Encoding.UTF8, "text/plain")
                };
            });

        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateClient(nameof(HttpRequestTool))).Returns(new HttpClient(handler.Object, disposeHandler: false));

        var options = Microsoft.Extensions.Options.Options.Create(new HttpRequestToolOptions
        {
            Enabled = true,
            AllowedHosts = new List<string> { "example.com" },
            AllowedMethods = new List<string> { "GET" },
            TimeoutMs = 10_000,
            MaxResponseBytes = 8_192
        });

        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var tool = new HttpRequestTool(factory.Object, options, NullLogger<HttpRequestTool>.Instance, exceptionHandler);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["url"] = "https://example.com/data",
            ["method"] = "GET"
        });

        result.Success.Should().BeFalse();
        calls.Should().Be(1);
    }
}
