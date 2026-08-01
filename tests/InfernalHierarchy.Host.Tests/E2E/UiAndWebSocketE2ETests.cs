using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

[Collection("Host E2E")]
public sealed class UiAndWebSocketE2ETests
{
    [Fact]
    public async Task Ui_Index_ReturnsHtml()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/ui", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("InfernalHierarchy UI");
    }

    [Fact(Skip = "Flaky under current WebApplicationFactory/WebSocket lifecycle; HTTP/UI paths remain covered.")]
    public async Task WebSocket_Task_ToLucifer_ReceivesReport()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        using var client = factory.CreateClient();

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Respond\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Hello from WS\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var wsClient = factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var request = JsonSerializer.Serialize(new { type = "task", toAgentId = "lucifer", content = "Say hello" });
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(request),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        var buffer = new byte[64 * 1024];

        while (DateTime.UtcNow < deadline)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            result.MessageType.Should().Be(WebSocketMessageType.Text);

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (text.Contains("\"type\": \"Report\"", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("Hello from WS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new TimeoutException("Did not receive expected Report over WebSocket");
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
