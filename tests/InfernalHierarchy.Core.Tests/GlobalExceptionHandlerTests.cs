using FluentAssertions;
using InfernalHierarchy.Core;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace InfernalHierarchy.Core.Tests;

public sealed class GlobalExceptionHandlerTests
{
    private sealed class TestIOException : IOException
    {
        public TestIOException()
        {
        }

        public TestIOException(string message)
            : base(message)
        {
        }

        public TestIOException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public TestIOException(int hResult)
        {
            HResult = hResult;
        }
    }

    [Fact]
    public async Task HandleExceptionAsync_WithTimeoutException_CategorizesAsTransientAndRetries()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var result = await handler.HandleExceptionAsync(
            new TimeoutException("timeout"),
            operationName: "op",
            correlationId: "corr-1");

        result.Category.Should().Be(ExceptionCategory.Transient);
        result.ShouldRetry.Should().BeTrue();
        result.CorrelationId.Should().Be("corr-1");
        result.Message.Should().Contain("temporarily");
        result.TechnicalDetails.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task HandleExceptionAsync_WithTransientHttpStatus_CategorizesAsTransient(HttpStatusCode statusCode)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var httpException = new HttpRequestException("http", inner: null, statusCode);
        var result = await handler.HandleExceptionAsync(httpException, operationName: "http", correlationId: "corr-http");

        result.Category.Should().Be(ExceptionCategory.Transient);
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task HandleExceptionAsync_WithNonTransientHttpStatus_CategorizesAsUnknown()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var httpException = new HttpRequestException("http", inner: null, HttpStatusCode.BadRequest);
        var result = await handler.HandleExceptionAsync(httpException, operationName: "http", correlationId: "corr-http");

        result.Category.Should().Be(ExceptionCategory.Unknown);
        result.ShouldRetry.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x21)]
    [InlineData(0x27)]
    [InlineData(0x6D)]
    public async Task HandleExceptionAsync_WithTransientIoHResult_CategorizesAsTransient(int lowWordHResult)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var ioException = new TestIOException(lowWordHResult);
        var result = await handler.HandleExceptionAsync(ioException, operationName: "io", correlationId: "corr-io");

        result.Category.Should().Be(ExceptionCategory.Transient);
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task HandleExceptionAsync_WithArgumentException_CategorizesAsBusinessAndDoesNotRetry()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var result = await handler.HandleExceptionAsync(
            new ArgumentException("bad"),
            operationName: "biz",
            correlationId: "corr-biz");

        result.Category.Should().Be(ExceptionCategory.Business);
        result.ShouldRetry.Should().BeFalse();
        result.Message.Should().Contain("Invalid operation");
    }

    [Fact]
    public async Task HandleExceptionAsync_WithAggregateExceptionSingleInner_UsesInnerCategory()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var aggregate = new AggregateException(new TimeoutException("timeout"));
        var result = await handler.HandleExceptionAsync(aggregate, operationName: "agg", correlationId: "corr-agg");

        result.Category.Should().Be(ExceptionCategory.Transient);
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task HandleExceptionAsync_WithFileNotFoundException_CategorizesAsSystem()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var result = await handler.HandleExceptionAsync(
            new FileNotFoundException("missing"),
            operationName: "sys",
            correlationId: "corr-sys");

        result.Category.Should().Be(ExceptionCategory.System);
        result.ShouldRetry.Should().BeFalse();
        result.Message.Should().Contain("system error");
    }
}
