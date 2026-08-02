using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class ReActTaskComplexityAdvisorTests
{
    private sealed class ConstantLlmClient : ILlmClient
    {
        private readonly string _response;

        public ConstantLlmClient(string response)
        {
            _response = response;
        }

        public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => Task.FromResult(_response);

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(_response);
    }

    [Fact]
    public void Assess_WhenShortAndLowRisk_ClassifiesSimple()
    {
        var options = new ReActOptions
        {
            SimpleTaskMaxIterations = 2,
            MediumTaskMaxIterations = 5,
            ComplexTaskMaxIterations = 8,
            HardMaxIterations = 8,
            MaxParallelBranches = 3
        };

        var assessment = ReActTaskComplexityAdvisor.Assess(
            task: "what is 2+2?",
            availableTools: ["read_memory"],
            executionProfile: "Research",
            options: options);

        assessment.Complexity.Should().Be(ReActTaskComplexity.Simple);
        assessment.IterationBudget.Should().Be(2);
        assessment.RecommendedParallelBranches.Should().Be(1);
    }

    [Fact]
    public void Assess_WhenBuildProfile_ClassifiesComplex()
    {
        var options = new ReActOptions
        {
            SimpleTaskMaxIterations = 3,
            MediumTaskMaxIterations = 5,
            ComplexTaskMaxIterations = 7,
            HardMaxIterations = 8,
            MaxParallelBranches = 4
        };

        var assessment = ReActTaskComplexityAdvisor.Assess(
            task: "run build and package",
            availableTools: ["workflow_step"],
            executionProfile: "Build",
            options: options);

        assessment.Complexity.Should().Be(ReActTaskComplexity.Complex);
        assessment.IterationBudget.Should().Be(7);
        assessment.RecommendedParallelBranches.Should().Be(4);
    }

    [Fact]
    public async Task LoopRunner_WhenSimpleBudgetIsTwo_StopsAfterTwoIterations()
    {
        var persona = new Persona
        {
            Name = "Tester",
            DemonTitle = "Tester",
            SystemPrompt = "",
            AvailableTools = ["read_memory"]
        };

        var llm = new ConstantLlmClient(
            "Thought: try\nAction: read_memory\nAction Input: {\"query\":\"x\"}");

        var toolRegistry = new Mock<IToolRegistry>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(x => x.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync(new ActionExecutionResult(
                ToolFound: false,
                Success: false,
                Observation: "Observation: Tool 'read_memory' unavailable",
                ToolCall: null,
                Error: "tool not found"));

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "sum numbers",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "tester",
            AgentName: "Tester",
            AgentRank: AgentRank.Worker,
            ReActOptions: new ReActOptions
            {
                UseJsonResponse = false,
                SimpleTaskMaxIterations = 2,
                MediumTaskMaxIterations = 5,
                ComplexTaskMaxIterations = 8,
                HardMaxIterations = 8
            },
            PromptBuilder: new DefaultReActPromptBuilder(),
            ExecutionProfile: "Research");

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.Iterations.Should().Be(2);
        result.FinalAnswer.Should().Contain("Task incomplete after 2 iterations");
    }
}