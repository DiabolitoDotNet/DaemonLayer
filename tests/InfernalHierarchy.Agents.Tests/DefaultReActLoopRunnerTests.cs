using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class DefaultReActLoopRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenModelRepeatsSameSuccessfulToolCall_SuppressesDuplicateInvocationAsync()
    {
        var persona = new Persona
        {
            Name = "Orobas",
            DemonTitle = "Orobas",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status"]
        };

        var llm = new SequenceLlmClient(
            "Thought: Check status\nAction: get_agent_status\nAction Input: {}",
            "Thought: Check status\nAction: get_agent_status\nAction Input: {}",
            "Thought: Done\nAction: FINAL_ANSWER\nAction Input: ok");

        var toolRegistry = new Mock<IToolRegistry>();

        var execCount = 0;
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync(() =>
            {
                execCount++;
                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: status=ok",
                    ToolCall: "get_agent_status({})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "probe",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "orobas",
            AgentName: "Orobas",
            AgentRank: AgentRank.Duke,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("ok");
        execCount.Should().Be(1);
        result.ToolCalls.Should().ContainSingle().Which.Should().Be("get_agent_status({})");
    }

    private sealed class SequenceLlmClient : ILlmClient
    {
        private readonly Queue<string> _responses;

        public SequenceLlmClient(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public Task<string> GetCompletionAsync(string systemPrompt, string prompt, CancellationToken ct)
        {
            _responses.Count.Should().BeGreaterThan(0, "test LLM responses should not be exhausted");
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => GetCompletionAsync(string.Empty, prompt, ct);
    }
}