using System.Net.Http.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Api;
using InfernalHierarchy.Host.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

[Collection("Host E2E")]
public sealed class ChatApiE2ETests
{
    [Fact]
    public async Task ApiChat_SimpleConversation_ReturnsReport()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Respond directly\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Hello from Lucifer\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Say hello",
            ToAgentId: "lucifer",
            TimeoutMs: 10_000));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body.Should().NotBeNull();
        body!.fromAgentId.Should().Be("lucifer");
        body.content.Should().Contain("Hello from Lucifer");
        body.correlationId.Should().NotBeNullOrWhiteSpace();
        response.Headers.Should().Contain(h => h.Key == "X-Correlation-Id");
    }

    [Fact]
    public async Task ApiChat_SearchThenEmail_SendsEmailWithResults()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Search the web\",\"action\":\"web_search\",\"actionInput\":{\"query\":\"OpenTelemetry dotnet\",\"count\":2}}",
            "{\"thought\":\"Email the findings\",\"action\":\"email_send\",\"actionInput\":{\"to\":\"user@example.com\",\"subject\":\"Search results\",\"body\":\"Results include: Example Result 1 (https://example.test/1)\"}}",
            "{\"thought\":\"Done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Sent the email with results.\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Use web_search then email_send",
            ToAgentId: "lucifer",
            TimeoutMs: 20_000));

        response.EnsureSuccessStatusCode();

        var mail = factory.Services.GetRequiredService<FakeEmailSender>();
        mail.Sent.Should().HaveCount(1);
        var msg = mail.Sent.Single();
        msg.To.Single().Address.Should().Be("user@example.com");
        msg.Subject.Should().Be("Search results");
        msg.Body.Should().Contain("Example Result 1");

        var searx = factory.Services.GetRequiredService<FakeSearXngClient>();
        searx.Calls.Should().NotBeEmpty();
        searx.Calls.Last().Query.Should().Contain("OpenTelemetry");
    }

    [Fact]
    public async Task ApiChat_InboxQuery_ReadsMailboxAndReturnsSummary()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Check inbox\",\"action\":\"email_inbox_query\",\"actionInput\":{\"from\":\"alerts@example.com\",\"unread_only\":true,\"max_results\":2}}",
            "{\"thought\":\"Summarize\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"I found inbox messages from alerts@example.com.\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Check if I have unread email from alerts@example.com",
            ToAgentId: "lucifer",
            TimeoutMs: 20_000));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body.Should().NotBeNull();
        body!.content.Should().Contain("inbox messages");

        var inbox = factory.Services.GetRequiredService<FakeEmailInboxQueryClient>();
        inbox.Calls.Should().HaveCount(1);
        inbox.Calls[0].FromFilter.Should().Be("alerts@example.com");
        inbox.Calls[0].UnreadOnly.Should().BeTrue();
        inbox.Calls[0].MaxResults.Should().Be(2);
    }

    [Fact]
    public async Task ApiChat_TeamWorkflow_CreatesAgents_Collaborates_Emails()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();

        // Team members respond to collaboration requests.
        llm.Enqueue("Baal",
            "{\"thought\":\"Provide a recommendation\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Use https://example.test/1 as a primary source.\"}");
        llm.Enqueue("Vassago",
            "{\"thought\":\"Provide a recommendation\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Use https://example.test/2 as a secondary source.\"}");

        // Lucifer orchestrates: create subagents, request collaboration, then email.
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Create Baal\",\"action\":\"create_sub_agent\",\"actionInput\":{\"persona_name\":\"Baal\",\"rank\":\"Prince\"}}",
            "{\"thought\":\"Create Vassago\",\"action\":\"create_sub_agent\",\"actionInput\":{\"persona_name\":\"Vassago\",\"rank\":\"Duke\"}}",
            "{\"thought\":\"Ask them to collaborate\",\"action\":\"request_collaboration\",\"actionInput\":{\"agent_id\":\"lucifer\",\"task\":\"Provide two sources about OpenTelemetry\",\"strategy\":\"weighted\",\"min_participants\":2,\"min_confidence\":0.0,\"participant_ranks\":\"prince,duke\"}}",
            "{\"thought\":\"Email the collaboration result\",\"action\":\"email_send\",\"actionInput\":{\"to\":\"user@example.com\",\"subject\":\"Team findings\",\"body\":\"Collaboration completed; see tool output above.\"}}",
            "{\"thought\":\"Done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Team workflow completed.\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Create a team, collaborate, and email me the result.",
            ToAgentId: "lucifer",
            TimeoutMs: 45_000));

        response.EnsureSuccessStatusCode();

        var mail = factory.Services.GetRequiredService<FakeEmailSender>();
        mail.Sent.Should().HaveCount(1);
        var msg = mail.Sent.Single();
        msg.Subject.Should().Be("Team findings");

        var registry = factory.Services.GetRequiredService<IAgentRegistry>();
        registry.GetAgentsByRank(InfernalHierarchy.Core.Entities.AgentRank.Prince)
            .Any(a => a.Name.Equals("Baal", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
        registry.GetAgentsByRank(InfernalHierarchy.Core.Entities.AgentRank.Duke)
            .Any(a => a.Name.Equals("Vassago", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ApiChat_WhenNonLocalWithoutOperatorKey_ReturnsUnauthorized()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Say hello",
            ToAgentId: "lucifer",
            TimeoutMs: 10_000));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiChat_Success_ShouldIncludeStructuredAutonomyOutcomeContract()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Respond directly\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Hello from contract test\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Return a quick answer",
            ToAgentId: "lucifer",
            TimeoutMs: 10_000));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body.Should().NotBeNull();
        body!.payload.Should().ContainKey("autonomy_outcome_status");
        body.payload.Should().ContainKey("autonomy_outcome_reason_code");
        body.payload.Should().ContainKey("autonomy_outcome_autonomous_success");
        body.payload.Should().ContainKey("autonomy_outcome_needs_supervisor_intervention");
        body.payload.Should().ContainKey("autonomy_outcome_next_action");
    }

    [Fact]
    public async Task ApiChat_Timeout_ShouldReturnStructuredAutonomyOutcomeContract()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "This should timeout because the target agent is not active",
            ToAgentId: "ghost-agent",
            TimeoutMs: 200));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.GatewayTimeout);

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body.Should().NotBeNull();
        body!.content.Should().Contain("Timeout:");
        body.payload["autonomy_outcome_status"].ToString().Should().Be("timeout");
        body.payload["autonomy_outcome_reason_code"].ToString().Should().Be("playground_timeout");
        body.payload["autonomy_outcome_next_action"].ToString().Should().Be("none");
        bool.Parse(body.payload["autonomy_outcome_needs_supervisor_intervention"].ToString()!).Should().BeFalse();
    }

    [Fact]
    public async Task ApiChat_ShouldNormalizeProfileMentionsToEffectiveExecutionProfile()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, "test-operator-key");

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Respond directly\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Note: custom tool creation is restricted under the Research profile.\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(
            Message: "Profile normalization check",
            ToAgentId: "lucifer",
            TimeoutMs: 10_000,
            ExecutionProfile: "Build"));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body.Should().NotBeNull();
        body!.content.Should().Contain("under the Build profile");
        body.content.Should().NotContain("under the Research profile");
        body.payload["execution_profile"].ToString().Should().Be("Build");
    }

    private static async Task WaitForAgentAsync(IServiceProvider services, string agentId)
    {
        var registry = services.GetRequiredService<IAgentRegistry>();
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (registry.IsRegistered(agentId))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Agent '{agentId}' was not registered in time");
    }
}
