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

    [Fact]
    public async Task AnalyzeAsync_MailboxIntent_ShouldDetectInboxCapabilityGap()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "email_send", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Check my inbox and tell me if I received mail from alerts@example.com"
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g => g.Capability == "mailbox_read");
        result.Report.Should().NotBeNull();
        result.Report!.CandidateTools.Should().Contain("email_inbox_query");
        result.Plan.Should().NotBeNull();
        result.Plan!.Steps.Should().Contain(s => s.Name == "retry_original_task");
    }

    [Fact]
    public async Task AnalyzeAsync_SensitiveCredentialIntent_ShouldSetHighRiskReport()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Use my login and password credentials to check mailbox"
        }, persona, CancellationToken.None);

        result.Report.Should().NotBeNull();
        result.Report!.SecurityRiskClass.Should().Be(CapabilitySecurityRiskClass.High);
        result.Report.CanAutofix.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ExternalProviderIntegration_ShouldRequireQualificationEvenWhenCollaborationToolExists()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "request_collaboration", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Integrate with Salesforce and configure OAuth flow for lead sync"
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g => g.Capability == "integration_qualification");
        result.Remediations.Should().Contain(r =>
            r.Kind == CapabilityRemediationActionKind.EscalateCollaboration
            && r.ReasonCode == "capability_qualification_required");
    }

    [Fact]
    public async Task AnalyzeAsync_FilesystemReadIntent_ShouldInferFilesystemReadCapabilityGap()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "read_memory", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Read the source files from the repo and summarize the findings"
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g => g.Capability == "filesystem_read");
    }

    [Fact]
    public async Task AnalyzeAsync_WorkflowIntent_ShouldInferWorkflowOrchestrationGap()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "read_memory", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Orchestrate release workflow steps for deployment"
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g => g.Capability == "workflow_orchestration");
    }

    [Fact]
    public async Task AnalyzeAsync_ConnectorOnboardingIntent_ShouldUseLowConfidenceQualificationFallback()
    {
        var analyzer = new DefaultCapabilityGapAnalyzer(skillPackCatalog: null);
        var context = BuildContext();

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares",
            AvailableTools = new[] { "request_collaboration", "create_custom_tool" }
        };

        var result = await analyzer.AnalyzeAsync(context, new AgentMessage
        {
            Content = "Onboard a connector to sync our internal CRM system"
        }, persona, CancellationToken.None);

        result.HasGaps.Should().BeTrue();
        result.Gaps.Should().Contain(g =>
            g.Capability == "integration_qualification"
            && (g.ReasonCode == "requires_capability_qualification" || g.ReasonCode == "low_confidence_capability_inference"));
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
