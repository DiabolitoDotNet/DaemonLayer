using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class CapabilityGapAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ShouldDetectMissingGraphQlCapability_AndSuggestRemediation()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "request_skill_pack", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Query the GraphQL endpoint to fetch release metadata."
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g => g.Capability == "graphql_access");
        result.Remediations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldNotFlagCommonPlanningTask()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "read_memory", "write_memory" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Produce a concise implementation plan and summarize trade-offs."
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeFalse();
        result.Remediations.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_WhenToolBlockedByProfile_ShouldSuggestProfileSwitch()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "graphql_request", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Run this GraphQL query for release metadata.",
            Payload = new Dictionary<string, object>
            {
                ["profile_allowed_tools"] = "read_memory,write_memory",
                ["execution_profile"] = "Research"
            }
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g =>
            g.Capability == "graphql_access"
            && g.BlockedByProfile
            && g.ReasonCode == "profile_constraint_blocked_tool");

        result.Remediations.Should().Contain(r =>
            r.Kind == CapabilityRemediationActionKind.SwitchExecutionProfile
            && r.ReasonCode == "switch_profile_required");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenRequiredToolsProvided_ShouldDetectGapWithoutKeyword()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "read_memory" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Please proceed with the task.",
            Payload = new Dictionary<string, object>
            {
                ["required_tools"] = "sql_query_readonly"
            }
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g =>
            g.Capability == "sql_read"
            && g.ReasonCode == "explicit_required_tool_missing");
    }

    [Fact]
    public async Task AnalyzeAsync_WithMultipleRules_ShouldKeepDeterministicOrder()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = Array.Empty<string>()
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Call the REST API endpoint then query GraphQL for release data."
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().HaveCountGreaterOrEqualTo(2);
        result.Gaps[0].Capability.Should().Be("http_api_integration");
        result.Gaps[1].Capability.Should().Be("graphql_access");
    }

    private static ReActTaskProcessorContext BuildContext()
    {
        return new ReActTaskProcessorContext(
            AgentId: "agent-1",
            AgentName: "Agares",
            AgentRank: AgentRank.Duke,
            Persona: new Persona(),
            LlmClient: Mock.Of<ILlmClient>(),
            ToolRegistry: Mock.Of<IToolRegistry>(),
            SharedMemory: Mock.Of<ISharedMemory>(),
            ActionParser: Mock.Of<IActionParser>(),
            ActionExecutor: Mock.Of<IActionExecutor>(),
            ReportGenerator: Mock.Of<IReportGenerator>(),
            PromptBuilder: Mock.Of<IReActPromptBuilder>(),
            LoopRunner: Mock.Of<IReActLoopRunner>(),
            ReActOptions: new ReActOptions(),
            RagOptions: new RagOptions(),
            VectorMemory: null,
            CollaborationService: null,
            RuntimeSkillStore: null,
            EventSink: null,
            SetStatus: _ => { },
            BuildBaseContextAsync: (_, _) => Task.FromResult("base"),
            Logger: Mock.Of<ILogger>());
    }
}
