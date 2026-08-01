using System.Net.Http.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Api;
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
    }

    [Fact]
    public async Task ApiChat_SearchThenEmail_SendsEmailWithResults()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

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
    public async Task ApiChat_TeamWorkflow_CreatesAgents_Collaborates_Emails()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

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
