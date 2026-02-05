using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace InfernalHierarchy.Host.Tests;

public class ResiliencePoliciesTests
{
    [Fact]
    public void Initialize_CreatesAllPolicies()
    {
        var logger = new Mock<ILogger<ResiliencePolicies>>();
        var policies = new ResiliencePolicies(logger.Object);

        policies.Initialize();

        policies.HttpRequestPolicy.Should().NotBeNull();
        policies.LlmCallPolicy.Should().NotBeNull();
        policies.DatabasePolicy.Should().NotBeNull();
        policies.ToolExecutionPolicy.Should().NotBeNull();
    }

    [Fact]
    public void Provider_ReturnsInitializedPolicies()
    {
        var logger = new Mock<ILogger<ResiliencePolicies>>();
        var policies = new ResiliencePolicies(logger.Object);

        var provider = new ResiliencePolicyProvider(policies);

        provider.GetHttpPolicy().Should().NotBeNull();
        provider.GetLlmPolicy().Should().NotBeNull();
        provider.GetDatabasePolicy().Should().NotBeNull();
        provider.GetToolExecutionPolicy().Should().NotBeNull();
    }

    [Fact]
    public async Task DatabasePolicy_WhenExceptionOccurs_ShouldRetry_WithoutSleeping_WhenCancelled()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;
        using var cts = new CancellationTokenSource();

        Func<CancellationToken, Task> action = _ =>
        {
            attempts++;
            cts.Cancel();
            throw new InvalidOperationException("boom");
        };

        var act = async () => await policies.DatabasePolicy.ExecuteAsync(action, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DatabasePolicy_WhenOperationCanceledException_ShouldNotRetry()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;

        Func<CancellationToken, Task> action = _ =>
        {
            attempts++;
            throw new OperationCanceledException();
        };

        var act = async () => await policies.DatabasePolicy.ExecuteAsync(action, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ToolExecutionPolicy_WhenArgumentException_ShouldNotRetry()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;

        Func<CancellationToken, Task> action = _ =>
        {
            attempts++;
            throw new ArgumentException("bad");
        };

        var act = async () => await policies.ToolExecutionPolicy.ExecuteAsync(action, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task LlmCallPolicy_WhenTaskCanceledButNotUserCancelled_ShouldRetry_WithoutSleeping_WhenCancelled()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;
        using var cts = new CancellationTokenSource();

        Func<CancellationToken, Task> action = _ =>
        {
            attempts++;
            cts.Cancel();
            throw new TaskCanceledException("timeout");
        };

        var act = async () => await policies.LlmCallPolicy.ExecuteAsync(action, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task LlmCallPolicy_WhenTaskCanceledBecauseUserCancelled_ShouldNotRetry()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;
        using var innerCts = new CancellationTokenSource();
        innerCts.Cancel();

        var canceledTask = Task.FromCanceled(innerCts.Token);

        Func<CancellationToken, Task> action = _ =>
        {
            attempts++;
            throw new TaskCanceledException(canceledTask);
        };

        var act = async () => await policies.LlmCallPolicy.ExecuteAsync(action, CancellationToken.None);

        await act.Should().ThrowAsync<TaskCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task HttpRequestPolicy_WhenResultIsFailure_ShouldHitRetryPath_WithoutSleeping_WhenCancelled()
    {
        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        policies.Initialize();

        var attempts = 0;
        using var cts = new CancellationTokenSource();

        var act = async () =>
            await policies.HttpRequestPolicy.ExecuteAsync(ct =>
            {
                attempts++;
                cts.Cancel();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }
}
