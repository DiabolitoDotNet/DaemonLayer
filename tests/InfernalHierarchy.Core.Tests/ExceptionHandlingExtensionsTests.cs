using FluentAssertions;
using InfernalHierarchy.Core.ErrorHandling;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
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
    public async Task ExecuteWithHandlingAsync_WithTransientException_WhenMaxRetriesExceeded_ThrowsWithoutRetrying()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        Func<CancellationToken, Task<int>> operation = _ =>
        {
            attempts++;
            throw new TimeoutException("timeout");
        };

        var act = async () => await handler.ExecuteWithHandlingAsync(operation, operationName: "op", maxRetries: 1, correlationId: "corr");

        await act.Should().ThrowAsync<TimeoutException>();
        attempts.Should().Be(1);

        handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Transient);
        handler.CorrelationIds.Should().ContainSingle().Which.Should().Be("corr");
    }

    [Fact]
    public async Task ExecuteWithHandlingAsync_WhenCorrelationIdNotProvided_GeneratesCorrelationId()
    {
        var handler = new CapturingExceptionHandler();

        Func<CancellationToken, Task<int>> operation = _ => throw new ArgumentException("bad");

        var act = async () => await handler.ExecuteWithHandlingAsync(operation, operationName: "op", maxRetries: 1, correlationId: null);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CorrelationIds.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
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

    [Fact]
    public async Task CreateRetryPolicy_WhenTimeoutException_RetriesOnce_AndInvokesHandler()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        var originalSleepAsync = Polly.Utilities.SystemClock.SleepAsync;
        Polly.Utilities.SystemClock.SleepAsync = (_, _) => Task.CompletedTask;

        try
        {
            var policy = handler.CreateRetryPolicy<int>(operationName: "op", maxRetries: 1);

            var result = await policy.ExecuteAsync(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new TimeoutException("timeout");
                }

                return Task.FromResult(7);
            }, new Context());

            result.Should().Be(7);
            attempts.Should().Be(2);
            handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Transient);
        }
        finally
        {
            Polly.Utilities.SystemClock.SleepAsync = originalSleepAsync;
        }
    }

    [Fact]
    public async Task CreateRetryPolicy_WhenHttpRequestExceptionIsTransient_Retries_AndUsesContextCorrelationId()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        var originalSleepAsync = Polly.Utilities.SystemClock.SleepAsync;
        Polly.Utilities.SystemClock.SleepAsync = (_, _) => Task.CompletedTask;

        try
        {
            var policy = handler.CreateRetryPolicy<int>(operationName: "op", maxRetries: 1);
            var context = new Context();

            var result = await policy.ExecuteAsync(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new HttpRequestException("server error", null, System.Net.HttpStatusCode.InternalServerError);
                }

                return Task.FromResult(123);
            }, context);

            result.Should().Be(123);
            attempts.Should().Be(2);
            handler.CorrelationIds.Should().ContainSingle().Which.Should().Be(context.CorrelationId.ToString());
            handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Transient);
        }
        finally
        {
            Polly.Utilities.SystemClock.SleepAsync = originalSleepAsync;
        }
    }

    [Fact]
    public async Task CreateRetryPolicy_WhenIOException_Retries()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        var originalSleepAsync = Polly.Utilities.SystemClock.SleepAsync;
        Polly.Utilities.SystemClock.SleepAsync = (_, _) => Task.CompletedTask;

        try
        {
            var policy = handler.CreateRetryPolicy<int>(operationName: "op", maxRetries: 1);

            var result = await policy.ExecuteAsync(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new SharingViolationIOException("io");
                }

                return Task.FromResult(9);
            }, new Context());

            result.Should().Be(9);
            attempts.Should().Be(2);
            handler.Categories.Should().ContainSingle().Which.Should().Be(ExceptionCategory.Transient);
        }
        finally
        {
            Polly.Utilities.SystemClock.SleepAsync = originalSleepAsync;
        }
    }

    private sealed class SharingViolationIOException : IOException
    {
        public SharingViolationIOException(string message)
            : base(message)
        {
            HResult = 0x20; // ERROR_SHARING_VIOLATION
        }
    }

    [Fact]
    public async Task CreateRetryPolicy_WhenOperationCanceledException_DoesNotRetry()
    {
        var handler = new CapturingExceptionHandler();
        var attempts = 0;

        var originalSleepAsync = Polly.Utilities.SystemClock.SleepAsync;
        Polly.Utilities.SystemClock.SleepAsync = (_, _) => Task.CompletedTask;

        try
        {
            var policy = handler.CreateRetryPolicy<int>(operationName: "op", maxRetries: 2);

            var act = async () =>
                await policy.ExecuteAsync(_ =>
                {
                    attempts++;
                    throw new OperationCanceledException("cancelled");
                }, new Context());

            await act.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(1);
            handler.Categories.Should().BeEmpty();
        }
        finally
        {
            Polly.Utilities.SystemClock.SleepAsync = originalSleepAsync;
        }
    }
}
