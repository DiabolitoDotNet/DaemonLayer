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
    public async Task RunAsync_WhenTaskAsksForAgentNamesAndStatuses_RendersFromGetAgentStatusAndStopsAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status", "read_memory"]
        };

        var llm = new SequenceLlmClient(
            "Thought: Get status\nAction: get_agent_status\nAction Input: {}",
            // This would normally cause extra iterations, but the runner should stop after tool success.
            "Thought: Try memory\nAction: read_memory\nAction Input: {\"query\":\"x\"}");

        var toolRegistry = new Mock<IToolRegistry>();

        var execCount = 0;
        var toolNames = new List<string>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                execCount++;
                toolNames.Add(ctx.ToolName);
                if (!string.Equals(ctx.ToolName, "get_agent_status", StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: false,
                        Observation: "Observation: unexpected tool call in test",
                        ToolCall: null,
                        Error: "unexpected tool call");
                }

                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: {\"agents\":[{\"name\":\"Orobas\",\"status\":\"Idle\"},{\"name\":\"Lucifer\",\"status\":\"ActingWithTool\"}]}",
                    ToolCall: "get_agent_status({})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "ok renvoi mais cette fois liste les tous par leur nom et leur status",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Contain("Orobas");
        result.FinalAnswer.Should().Contain("Idle");
        result.FinalAnswer.Should().Contain("Lucifer");
        result.FinalAnswer.Should().Contain("ActingWithTool");
        execCount.Should().Be(1);
        toolNames.Should().ContainSingle().Which.Should().Be("get_agent_status");
        result.ToolCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_WhenTaskAsksListByNameAndStatus_ReturnsFormattedListAfterGetAgentStatusAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status"]
        };

        var llm = new SequenceLlmClient(
            "Thought: Fetch\nAction: get_agent_status\nAction Input: {}",
            // Should not be reached if shortcut works
            "Thought: Should not run\nAction: FINAL_ANSWER\nAction Input: nope");

        var toolRegistry = new Mock<IToolRegistry>();

        var execCount = 0;
        var toolNames = new List<string>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                execCount++;
                toolNames.Add(ctx.ToolName);
                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: {\"agents\":[{\"name\":\"Orobas\",\"status\":\"Idle\"},{\"name\":\"Lucifer\",\"status\":\"ActingWithTool\"}]}",
                    ToolCall: "get_agent_status({})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "renvoi mais cette fois liste les tous par leur nom et leur status",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        execCount.Should().Be(1);
        toolNames.Should().ContainSingle().Which.Should().Be("get_agent_status");
        result.Iterations.Should().Be(1);
        result.FinalAnswer.Should().Contain("Voici la liste des agents");
        result.FinalAnswer.Should().Contain("- Orobas — Idle");
        result.FinalAnswer.Should().Contain("- Lucifer — ActingWithTool");
    }

    [Fact]
    public async Task RunAsync_WhenTaskAsksListByNameAndStatus_RejectsDetoursAndForcesGetAgentStatusAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status", "read_memory"]
        };

        var llm = new SequenceLlmClient(
            // Model attempts a detour, but the runner should override to get_agent_status.
            "Thought: Maybe memory\nAction: read_memory\nAction Input: {\"query\":\"agents_list\"}");

        var toolRegistry = new Mock<IToolRegistry>();

        var execCount = 0;
        var toolNames = new List<string>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                execCount++;
                toolNames.Add(ctx.ToolName);

                ctx.ToolName.Should().Be("get_agent_status");
                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: {\"agents\":[{\"name\":\"Orobas\",\"status\":\"Idle\"},{\"name\":\"Lucifer\",\"status\":\"ActingWithTool\"}]}",
                    ToolCall: "get_agent_status({})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "ok renvoi mais cette fois liste les tous par leur nom et leur status",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        execCount.Should().Be(1);
        toolNames.Should().ContainSingle().Which.Should().Be("get_agent_status");
        result.Iterations.Should().Be(1);
        result.FinalAnswer.Should().Contain("Voici la liste des agents");
        result.FinalAnswer.Should().Contain("- Orobas — Idle");
        result.FinalAnswer.Should().Contain("- Lucifer — ActingWithTool");
    }

    [Fact]
    public async Task RunAsync_WhenTaskAsksToSendListByEmail_ForcesStatusThenSendsEmailAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status", "email_send", "read_memory"]
        };

        // Model tries a detour; runner should override to get_agent_status and then send email itself.
        var llm = new SequenceLlmClient(
            "Thought: Maybe memory\nAction: read_memory\nAction Input: {\"query\":\"x\"}");

        var toolRegistry = new Mock<IToolRegistry>();

        var calls = new List<string>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                calls.Add(ctx.ToolName);

                if (string.Equals(ctx.ToolName, "get_agent_status", StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: true,
                        Observation: "Observation: {\"agents\":[{\"name\":\"Orobas\",\"status\":\"Idle\"}]}",
                        ToolCall: "get_agent_status({})",
                        Error: null);
                }

                if (string.Equals(ctx.ToolName, "email_send", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.ActionInputText.Should().Contain("Liste des agents");
                    ctx.ActionInputText.Should().Contain("Orobas");

                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: true,
                        Observation: "Observation: Email sent",
                        ToolCall: "email_send({..})",
                        Error: null);
                }

                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: false,
                    Observation: "Observation: unexpected tool",
                    ToolCall: null,
                    Error: "unexpected tool");
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "envoi cette liste par mail",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        calls.Should().Equal("get_agent_status", "email_send");
        result.FinalAnswer.Should().Contain("l’email a bien été envoyé");
        result.ToolCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_WhenTaskAsksToCreateCustomTool_ForcesCreateCustomToolInsteadOfAdviceAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["create_custom_tool", "web_search"]
        };

        // Model tries to provide advice instead of acting.
        var llm = new SequenceLlmClient(
            "Thought: You can do it\nAction: FINAL_ANSWER\nAction Input: You can create custom tools using documentation.");

        var toolRegistry = new Mock<IToolRegistry>();

        var calls = new List<string>();
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                calls.Add(ctx.ToolName);

                ctx.ToolName.Should().Be("create_custom_tool");
                ctx.ActionInputText.Should().Contain("description");

                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: Created and registered tool 'custom_lacale_api'.",
                    ToolCall: "create_custom_tool({..})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "Crée un custom tool pour utiliser l'API La Cale dans DaemonLayer.",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        calls.Should().ContainSingle().Which.Should().Be("create_custom_tool");
        result.FinalAnswer.Should().Contain("custom_lacale_api");
        result.ToolCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_WhenEmailSendSucceeds_StopsImmediatelyToAvoidDuplicateEmailsAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["email_send"]
        };

        // Model tries to send twice with different timestamps.
        var llm = new SequenceLlmClient(
            "Thought: Send\nAction: email_send\nAction Input: {\"to\":\"Email:DefaultTo\",\"subject\":\"Test\",\"body\":\"Hello\",\"timestamp\":\"t1\"}",
            "Thought: Send again\nAction: email_send\nAction Input: {\"to\":\"Email:DefaultTo\",\"subject\":\"Test\",\"body\":\"Hello\",\"timestamp\":\"t2\"}");

        var toolRegistry = new Mock<IToolRegistry>();
        var execCount = 0;

        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                execCount++;
                ctx.ToolName.Should().Be("email_send");
                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: Email sent",
                    ToolCall: "email_send({..})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "envoie un email",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false, TerminalTools = Array.Empty<string>() },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        execCount.Should().Be(1);
        result.ToolCalls.Should().ContainSingle();
        result.FinalAnswer.Should().Contain("l’email a bien été envoyé");
    }

    [Fact]
    public async Task RunAsync_WhenGetAgentStatusUsesEmptyAndEmptyObjectInputs_SuppressesDuplicatesAsync()
    {
        var persona = new Persona
        {
            Name = "Orobas",
            DemonTitle = "Orobas",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status"]
        };

        var llm = new SequenceLlmClient(
            // Empty input (no JSON)
            "Thought: Check status\nAction: get_agent_status\nAction Input:",
            // Same call, but as an explicit empty object
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
    }

    [Fact]
    public async Task RunAsync_WhenAgentCountEmailTriesEmailSendFirst_ForcesGetAgentStatusFirstAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status", "email_send"]
        };

        var llm = new SequenceLlmClient(
            // First attempt incorrectly tries email_send without status.
            "{\"thought\":\"send\",\"action\":\"email_send\",\"actionInput\":{\"subject\":\"x\",\"body\":\"total=${total_agents}\"}}",
            // Then it follows the rule.
            "{\"thought\":\"get status\",\"action\":\"get_agent_status\",\"actionInput\":{\"query\":\"all\"}}",
            "{\"thought\":\"send\",\"action\":\"email_send\",\"actionInput\":{\"subject\":\"x\",\"body\":\"total_agents=${total_agents}, occupied_agents=${occupied_agents}, idle_agents=${idle_agents}\"}}"
        );

        var toolRegistry = new Mock<IToolRegistry>();

        var executor = new Mock<IActionExecutor>();
        var execCalls = new List<string>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                execCalls.Add(ctx.ToolName);

                if (ctx.ToolName.Equals("get_agent_status", StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: true,
                        Observation: "Observation: {\"total_agents\":5,\"occupied_agents\":1,\"idle_agents\":4}",
                        ToolCall: "get_agent_status({query:all})",
                        Error: null);
                }

                return new ActionExecutionResult(
                    ToolFound: true,
                    Success: true,
                    Observation: "Observation: Email sent",
                    ToolCall: "email_send({subject:x})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "envoi moi un mail avec le decompte des agents actifs",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("C’est fait — l’email a bien été envoyé.");
        execCalls.Should().ContainInOrder("get_agent_status", "email_send");
    }

    [Fact]
    public async Task RunAsync_WhenEmailBodyContainsAgentCountPlaceholders_RendersCountsFromLastAgentStatusObservationAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["get_agent_status", "email_send"]
        };

        var llm = new SequenceLlmClient(
            "{\"thought\":\"need counts\",\"action\":\"get_agent_status\",\"actionInput\":{\"query\":\"all\"}}",
            "{\"thought\":\"send mail\",\"action\":\"email_send\",\"actionInput\":{\"to\":\"http-req-123\",\"subject\":\"Active Agents Count\",\"body\":\"total_agents=${total_agents}, occupied_agents=${occupied_agents}, idle_agents=${idle_agents}\"}}"
        );

        var toolRegistry = new Mock<IToolRegistry>();

        ActionExecutionContext? capturedEmailContext = null;
        var executor = new Mock<IActionExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
            .ReturnsAsync((ActionExecutionContext ctx) =>
            {
                if (ctx.ToolName.Equals("get_agent_status", StringComparison.OrdinalIgnoreCase))
                {
                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: true,
                        Observation: "Observation: {\"total_agents\":5,\"occupied_agents\":1,\"idle_agents\":4}",
                        ToolCall: "get_agent_status({query:all})",
                        Error: null);
                }

                if (ctx.ToolName.Equals("email_send", StringComparison.OrdinalIgnoreCase))
                {
                    capturedEmailContext = ctx;
                    return new ActionExecutionResult(
                        ToolFound: true,
                        Success: true,
                        Observation: "Observation: Email sent",
                        ToolCall: "email_send({subject:x})",
                        Error: null);
                }

                return new ActionExecutionResult(
                    ToolFound: false,
                    Success: false,
                    Observation: "Observation: unexpected",
                    ToolCall: null,
                    Error: "unexpected");
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "envoi moi un mail avec le decompte des agents actifs",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("C’est fait — l’email a bien été envoyé.");
        capturedEmailContext.Should().NotBeNull();
        capturedEmailContext!.ActionInputObject.Should().NotBeNull();
        var body = capturedEmailContext.ActionInputObject!["body"].ToString();
        body.Should().Contain("total_agents=5");
        body.Should().Contain("occupied_agents=1");
        body.Should().Contain("idle_agents=4");
        body.Should().NotContain("${");
    }

    [Fact]
    public async Task RunAsync_WhenResponseIsUnparseable_AttemptsFormatRepairAndContinuesAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["email_send"]
        };

        // First response is unparseable; second response is the format-repair JSON.
        var llm = new SequenceLlmClient(
            "I will do it now.",
            "{\"thought\":\"done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"ok\"}");

        var toolRegistry = new Mock<IToolRegistry>();

        var executor = new Mock<IActionExecutor>();

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
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("ok");
        result.Iterations.Should().Be(1);
        result.ToolCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenEmailSendSucceeds_StopsImmediatelyAndReturnsFinalAnswerAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["email_send"]
        };

        var llm = new SequenceLlmClient(
            "Thought: Send reminder\nAction: email_send\nAction Input: {\"subject\":\"x\",\"body\":\"y\"}");

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
                    Observation: "Observation: Email sent",
                    ToolCall: "email_send({subject:x})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "envoi moi un mail",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("C’est fait — l’email a bien été envoyé.");
        execCount.Should().Be(1);
        result.ToolCalls.Should().ContainSingle().Which.Should().Be("email_send({subject:x})");
    }

    [Fact]
    public async Task RunAsync_WhenSendTelegramSucceeds_StopsImmediatelyAndReturnsObservationTextAsync()
    {
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "Lucifer",
            SystemPrompt = "",
            AvailableTools = ["send_telegram"]
        };

        var llm = new SequenceLlmClient(
            "Thought: Notify\nAction: send_telegram\nAction Input: {\"chat_id\":123,\"text\":\"ok\"}");

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
                    Observation: "Observation: Message queued for Telegram chat 123",
                    ToolCall: "send_telegram({chat_id:123})",
                    Error: null);
            });

        var context = new ReActLoopContext(
            SystemContext: "",
            Task: "notify telegram",
            Persona: persona,
            LlmClient: llm,
            ToolRegistry: toolRegistry.Object,
            ActionParser: new DefaultActionParser(),
            ActionExecutor: executor.Object,
            Logger: NullLogger.Instance,
            SetStatus: _ => { },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            ReActOptions: new ReActOptions { UseJsonResponse = false },
            PromptBuilder: new DefaultReActPromptBuilder());

        var runner = new DefaultReActLoopRunner();
        var result = await runner.RunAsync(context, CancellationToken.None);

        result.FinalAnswer.Should().Be("Message queued for Telegram chat 123");
        execCount.Should().Be(1);
        result.ToolCalls.Should().ContainSingle().Which.Should().Be("send_telegram({chat_id:123})");
    }

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