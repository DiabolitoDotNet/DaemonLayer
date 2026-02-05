using FluentAssertions;
using InfernalHierarchy.Core;
using InfernalHierarchy.Core.CQRS;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Saga;
using Xunit;

using CoreTaskStatus = InfernalHierarchy.Core.Entities.TaskStatus;

namespace InfernalHierarchy.Core.Tests;

public sealed class CqrsContractsTests
{
    [Fact]
    public void Commands_ShouldHaveDefaultIdsAndTimestamps()
    {
        var createAgent = new CreateAgentCommand { PersonaName = "lucifer" };
        var terminateAgent = new TerminateAgentCommand { AgentId = "agent_1" };
        var addFact = new AddFactCommand { Content = "c", Source = "s" };
        var requestCollab = new RequestCollaborationCommand { InitiatorAgentId = "lucifer", Task = "t" };
        var execTool = new ExecuteToolCommand { AgentId = "lucifer", ToolName = "web_search" };

        createAgent.ParentAgentId.Should().BeNull();
        createAgent.Name.Should().BeNull();

        var createWithOverrides = createAgent with { ParentAgentId = "parent_1", Name = "MyAgent" };
        createWithOverrides.PersonaName.Should().Be("lucifer");
        createWithOverrides.ParentAgentId.Should().Be("parent_1");
        createWithOverrides.Name.Should().Be("MyAgent");

        createAgent.CommandId.Should().NotBeNullOrWhiteSpace();
        terminateAgent.CommandId.Should().NotBeNullOrWhiteSpace();
        addFact.CommandId.Should().NotBeNullOrWhiteSpace();
        requestCollab.CommandId.Should().NotBeNullOrWhiteSpace();
        execTool.CommandId.Should().NotBeNullOrWhiteSpace();

        createAgent.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        terminateAgent.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        addFact.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        requestCollab.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        execTool.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        addFact.Tags.Should().NotBeNull();
        requestCollab.ParticipantAgentIds.Should().NotBeNull();
        execTool.Parameters.Should().NotBeNull();
    }

    [Fact]
    public void Queries_ShouldHaveDefaultIds_AndHoldParameters()
    {
        var getById = new GetAgentByIdQuery { AgentId = "agent_1" };
        var byRank = new GetAgentsByRankQuery { Rank = AgentRank.Duke };
        var hierarchy = new GetAgentHierarchyQuery { RootAgentId = null };
        var searchFacts = new SearchFactsQuery { SearchText = "needle" };
        var decisions = new GetDecisionsByAgentQuery { AgentId = "lucifer" };
        var tasks = new GetTasksByStatusQuery { Status = CoreTaskStatus.InProgress, AssignedTo = "lucifer" };
        var history = new GetCollaborationHistoryQuery { AgentId = "lucifer", Strategy = CollaborationStrategy.Voting };
        var stats = new GetAgentStatisticsQuery { AgentId = "lucifer" };

        getById.QueryId.Should().NotBeNullOrWhiteSpace();
        byRank.QueryId.Should().NotBeNullOrWhiteSpace();
        hierarchy.QueryId.Should().NotBeNullOrWhiteSpace();
        searchFacts.QueryId.Should().NotBeNullOrWhiteSpace();
        decisions.QueryId.Should().NotBeNullOrWhiteSpace();
        tasks.QueryId.Should().NotBeNullOrWhiteSpace();
        history.QueryId.Should().NotBeNullOrWhiteSpace();
        stats.QueryId.Should().NotBeNullOrWhiteSpace();

        getById.AgentId.Should().Be("agent_1");
        byRank.Rank.Should().Be(AgentRank.Duke);
        hierarchy.RootAgentId.Should().BeNull();
        searchFacts.SearchText.Should().Be("needle");
        decisions.AgentId.Should().Be("lucifer");
        tasks.Status.Should().Be(CoreTaskStatus.InProgress);
        tasks.AssignedTo.Should().Be("lucifer");
        history.AgentId.Should().Be("lucifer");
        history.Strategy.Should().Be(CollaborationStrategy.Voting);
        stats.AgentId.Should().Be("lucifer");

        var hierarchyResult = new AgentHierarchyResult();
        hierarchyResult.AllAgents.Should().NotBeNull();
        hierarchyResult.Relationships.Should().NotBeNull();

        hierarchyResult.Root.Should().BeNull();
        hierarchyResult.Root = new Agent { Id = "root", Name = "Root", Rank = AgentRank.Supreme };
        hierarchyResult.Root.Id.Should().Be("root");

        var agentStatistics = new AgentStatistics
        {
            AgentId = "lucifer",
            TasksCompleted = 7,
            DecisionsMade = 11,
            ToolExecutions = 5,
            AverageConfidence = 0.82,
            ChildAgentCount = 3,
            AverageTaskCompletionMs = 1234.5,
        };

        agentStatistics.AgentId.Should().Be("lucifer");
        agentStatistics.TasksCompleted.Should().Be(7);
        agentStatistics.DecisionsMade.Should().Be(11);
        agentStatistics.ToolExecutions.Should().Be(5);
        agentStatistics.AverageConfidence.Should().BeApproximately(0.82, 0.0001);
        agentStatistics.ChildAgentCount.Should().Be(3);
        agentStatistics.AverageTaskCompletionMs.Should().BeApproximately(1234.5, 0.0001);
    }

    [Fact]
    public void AgentState_ShouldHaveSaneDefaults_AndAllowMutation()
    {
        var state = new AgentState();

        state.AgentId.Should().BeEmpty();
        state.Terminated.Should().BeNull();

        state.AgentId = "a1";
        state.Created = DateTime.UnixEpoch;
        state.LastEventTimestamp = DateTime.UnixEpoch.AddSeconds(5);
        state.EventCount = 3;
        state.TasksReceived = 2;
        state.TasksCompleted = 1;
        state.ToolExecutions = 4;
        state.DecisionsMade = 5;

        state.AgentId.Should().Be("a1");
        state.EventCount.Should().Be(3);
        state.TasksCompleted.Should().Be(1);
    }

    [Fact]
    public void QueryRecords_ShouldSupportRecordSemantics()
    {
        List<string> tags = ["a", "b"];

        var q1 = new SearchFactsQuery
        {
            QueryId = "q1",
            SearchText = "needle",
            MinimumConfidence = 0.42,
            Tags = tags,
        };

        var q2 = new SearchFactsQuery
        {
            QueryId = "q1",
            SearchText = "needle",
            MinimumConfidence = 0.42,
            Tags = tags,
        };

        (q1 == q2).Should().BeTrue();
        q1.Equals(q2).Should().BeTrue();
        q1.GetHashCode().Should().Be(q2.GetHashCode());
        q1.ToString().Should().Contain("SearchText = needle");

        var q2DifferentTagsInstance = q2 with { Tags = ["a", "b"] };
        q2DifferentTagsInstance.Should().NotBe(q1);

        var q3 = q1 with { MinimumConfidence = 0.99, Tags = null };
        q3.Should().NotBe(q1);
        q3.MinimumConfidence.Should().Be(0.99);
        q3.Tags.Should().BeNull();

        var d1 = new GetDecisionsByAgentQuery
        {
            QueryId = "d",
            AgentId = "lucifer",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
        };

        var d2 = new GetDecisionsByAgentQuery
        {
            QueryId = "d",
            AgentId = "lucifer",
            StartTime = d1.StartTime,
            EndTime = d1.EndTime,
        };

        d1.Should().Be(d2);
        (d1 != d2).Should().BeFalse();
        d1.ToString().Should().Contain("AgentId = lucifer");

        var d3 = d1 with { EndTime = null };
        d3.EndTime.Should().BeNull();
        d3.GetHashCode().Should().NotBe(0);
    }

    [Fact]
    public void SagaContext_AndResult_ShouldBeUsableAsPoco()
    {
        var context = new SagaContext
        {
            SagaId = "s1",
            CurrentStep = 2,
            ErrorMessage = "boom",
            EndTime = DateTime.UtcNow,
        };

        context.Data["k"] = "v";
        context.CompletedSteps.Add("a");
        context.CompensatedSteps.Add("b");

        var result = new SagaResult
        {
            Success = false,
            Context = context,
            ErrorMessage = context.ErrorMessage,
            CompensationSuccess = true,
            ExecutionTime = TimeSpan.FromMilliseconds(123)
        };

        result.Success.Should().BeFalse();
        result.Context.SagaId.Should().Be("s1");
        result.ExecutionTime.Should().Be(TimeSpan.FromMilliseconds(123));
        SagaStatus.Compensated.Should().Be(SagaStatus.Compensated);
    }
}
