using FluentAssertions;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Host.Observability;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class OperatorExplainabilityServiceTests
{
    [Fact]
    public void BuildReport_ShouldMapCategoriesToExpectedKinds()
    {
        var sut = new OperatorExplainabilityService();
        var events = new[]
        {
            new AgentEvent
            {
                AgentId = "agent-a",
                Type = EventType.DecisionMade,
                Description = "Capability remediation action",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.remediation",
                    ["action_kind"] = "CreateCustomTool",
                    ["reason_code"] = "synthesize_custom_tool",
                    ["capability"] = "graphql_access",
                    ["task_id"] = "task-1"
                }
            },
            new AgentEvent
            {
                AgentId = "replay-agent",
                Type = EventType.DecisionMade,
                Description = "Dead-letter replay",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "deadletter.replay",
                    ["status"] = "failed",
                    ["reason_code"] = "replay_exception",
                    ["deadletter_id"] = "dl-1",
                    ["operation_name"] = "message_bus_publish"
                }
            },
            new AgentEvent
            {
                AgentId = "Belial",
                Type = EventType.DecisionMade,
                Description = "Supervisor intervention",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "supervisor.intervention",
                    ["supervisor_action"] = "preempt",
                    ["target_agent_id"] = "agent-z",
                    ["reason_code"] = "branch_preempted_after_stall"
                }
            }
        };

        var report = sut.BuildReport(events, maxItems: 50);

        report.Items.Should().HaveCount(3);
        report.Items.Should().Contain(i => i.Kind == "tool_or_skill_creation");
        report.Items.Should().Contain(i => i.Kind == "deadletter_replay");
        report.Items.Should().Contain(i => i.Kind == "branch_preempted");

        report.Summary["tool_or_skill_creation"].Should().Be(1);
        report.Summary["deadletter_replay"].Should().Be(1);
        report.Summary["branch_preempted"].Should().Be(1);
    }
}