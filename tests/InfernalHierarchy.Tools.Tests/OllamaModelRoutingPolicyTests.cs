using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Options;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class OllamaModelRoutingPolicyTests
{
    [Fact]
    public void ResolveModel_WhenRoutingDisabled_ReturnsDefaultModel()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = false,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "voice", MaxLatencyMs = 1500, Model = "gemma:2b", Priority = 10 }
            ]
        };

        var model = OllamaModelRoutingPolicy.ResolveModel(options, new LlmRoutingHint { TaskType = "voice", LatencyBudgetMs = 800 });

        model.Should().Be("qwen3:8b");
    }

    [Fact]
    public void ResolveModel_WhenExactTaskAndBudgetMatch_ReturnsBudgetedModel()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = true,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "voice", MaxLatencyMs = 1500, Model = "gemma:2b", Priority = 10 },
                new OllamaModelRoute { TaskType = "voice", MaxLatencyMs = 0, Model = "qwen3:8b", Priority = 20 }
            ]
        };

        var model = OllamaModelRoutingPolicy.ResolveModel(options, new LlmRoutingHint { TaskType = "voice", LatencyBudgetMs = 900 });

        model.Should().Be("gemma:2b");
    }

    [Fact]
    public void ResolveModel_WhenTaskMatchesButBudgetTooHigh_FallsBackToTaskCatchAll()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = true,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "voice", MaxLatencyMs = 1200, Model = "gemma:2b", Priority = 10 },
                new OllamaModelRoute { TaskType = "voice", MaxLatencyMs = 0, Model = "qwen3:8b", Priority = 20 }
            ]
        };

        var model = OllamaModelRoutingPolicy.ResolveModel(options, new LlmRoutingHint { TaskType = "voice", LatencyBudgetMs = 3000 });

        model.Should().Be("qwen3:8b");
    }

    [Fact]
    public void ResolveModel_WhenNoTaskSpecificRoute_UsesWildcardRoute()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = true,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "*", MaxLatencyMs = 0, Model = "dolphin3:8b", Priority = 30 }
            ]
        };

        var model = OllamaModelRoutingPolicy.ResolveModel(options, new LlmRoutingHint { TaskType = "coding", LatencyBudgetMs = 1000 });

        model.Should().Be("dolphin3:8b");
    }

    [Fact]
    public void ResolveModel_WithAdaptiveFeedback_PrefersLowerPenaltyModel()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = true,
            EnableAdaptiveRoutingFeedback = true,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "coding", MaxLatencyMs = 0, Model = "slow-model", Priority = 10 },
                new OllamaModelRoute { TaskType = "coding", MaxLatencyMs = 0, Model = "fast-model", Priority = 20 }
            ]
        };

        var feedback = new InMemoryModelRoutingFeedbackStore();
        for (var i = 0; i < 10; i++)
        {
            feedback.RecordOutcome("slow-model", success: true, TimeSpan.FromMilliseconds(5000), outputTokens: 100);
            feedback.RecordOutcome("fast-model", success: true, TimeSpan.FromMilliseconds(600), outputTokens: 100);
        }

        var model = OllamaModelRoutingPolicy.ResolveModel(
            options,
            new LlmRoutingHint { TaskType = "coding", LatencyBudgetMs = 1500 },
            feedback);

        model.Should().Be("fast-model");
    }

    [Fact]
    public void ResolveModel_WithAdaptiveFeedbackDisabled_KeepsPriorityOrder()
    {
        var options = new OllamaOptions
        {
            DefaultModel = "qwen3:8b",
            EnableModelRoutingPolicy = true,
            EnableAdaptiveRoutingFeedback = false,
            ModelRoutes =
            [
                new OllamaModelRoute { TaskType = "coding", MaxLatencyMs = 0, Model = "slow-model", Priority = 10 },
                new OllamaModelRoute { TaskType = "coding", MaxLatencyMs = 0, Model = "fast-model", Priority = 20 }
            ]
        };

        var feedback = new InMemoryModelRoutingFeedbackStore();
        for (var i = 0; i < 5; i++)
        {
            feedback.RecordOutcome("slow-model", success: false, TimeSpan.FromMilliseconds(5000), outputTokens: 0);
            feedback.RecordOutcome("fast-model", success: true, TimeSpan.FromMilliseconds(400), outputTokens: 120);
        }

        var model = OllamaModelRoutingPolicy.ResolveModel(
            options,
            new LlmRoutingHint { TaskType = "coding", LatencyBudgetMs = 1500 },
            feedback);

        model.Should().Be("slow-model");
    }
}
