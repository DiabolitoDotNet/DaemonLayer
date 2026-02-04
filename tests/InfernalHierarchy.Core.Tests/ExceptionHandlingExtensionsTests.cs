using FluentAssertions;
using InfernalHierarchy.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Core.Tests;

public sealed class ExceptionHandlingExtensionsTests
{
    private sealed class CapturingExceptionHandler : GlobalExceptionHandler
    {
        public CapturingExceptionHandler()
            : base(NullLogger<GlobalExceptionHandler>.Instance)
        {
        }

        public List<string> CorrelationIds { get; } = new();
        public List<ExceptionCategory> Categories { get; } = new();

        protected override Task OnExceptionHandledAsync(
            Exception exception,
            ExceptionCategory category,
            string correlationId,
            CancellationToken ct)
        {
            CorrelationIds.Add(correlationId);
            Categories.Add(category);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteWithHandlingAsync_WithBusinessException_DoesNotRetry()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        Func<CancellationToken, Task<int>> operation = _ =>
        {
            attempts++;
            throw new ArgumentException("bad");
        };

        var act = async () => await handler.ExecuteWithHandlingAsync(operation, operationName: "op", maxRetries: 3, correlationId: "corr");

        await act.Should().ThrowAsync<ArgumentException>();
        attempts.Should().Be(1);

        handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Business);
        handler.CorrelationIds.Should().ContainSingle().Which.Should().Be("corr");
    }

    [Fact]
    public async Task ExecuteWithHandlingAsync_WithTransientException_RetriesUntilSuccess()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        Func<CancellationToken, Task<int>> operation = _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("timeout");
            }

            return Task.FromResult(42);
        };

        var result = await handler.ExecuteWithHandlingAsync(operation, operationName: "op", maxRetries: 2, correlationId: "corr");

        result.Should().Be(42);
        attempts.Should().Be(2);
        handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Transient);
        handler.CorrelationIds.Should().ContainSingle().Which.Should().Be("corr");
    }

    [Fact]
    public async Task ExecuteWithHandlingAsync_VoidOverload_ExecutesOperation()
    {
        var handler = new CapturingExceptionHandler();
        var executed = false;

        await handler.ExecuteWithHandlingAsync(
            _ =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            operationName: "void-op",
            maxRetries: 1,
            correlationId: "corr");

        executed.Should().BeTrue();
    }
}
