using System.Net;
using System.Text;
using FluentAssertions;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Tools.Clients.Search;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class SearXngClientResilienceTests
{
    [Fact]
    public async Task SearchAsync_WhenTransientFailure_ShouldRetryAndSucceed()
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
                    Content = new StringContent("{" + "\"results\":[{\"title\":\"A\",\"url\":\"https://a\",\"content\":\"x\"}]}", Encoding.UTF8, "application/json")
                };
            });

        using var httpClient = new HttpClient(handler.Object, disposeHandler: false);
        var options = Microsoft.Extensions.Options.Options.Create(new SearXNGOptions { BaseUrl = new Uri("http://localhost:8080") });
        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var sut = new SearXngClient(httpClient, options, NullLogger<SearXngClient>.Instance, exceptionHandler);

        var result = await sut.SearchAsync("test", 5, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Results.Should().ContainSingle();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_WhenPermanentFailure_ShouldNotRetry()
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

        using var httpClient = new HttpClient(handler.Object, disposeHandler: false);
        var options = Microsoft.Extensions.Options.Options.Create(new SearXNGOptions { BaseUrl = new Uri("http://localhost:8080") });
        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var sut = new SearXngClient(httpClient, options, NullLogger<SearXngClient>.Instance, exceptionHandler);

        var result = await sut.SearchAsync("test", 5, CancellationToken.None);

        result.Error.Should().NotBeNull();
        calls.Should().Be(1);
    }
}
