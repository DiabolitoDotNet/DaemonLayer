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
