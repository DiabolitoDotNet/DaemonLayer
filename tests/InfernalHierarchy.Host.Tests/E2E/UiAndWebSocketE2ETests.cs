using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Security;
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

    [Fact]
    public async Task WebSocket_Task_ToLucifer_ReceivesReport()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        using var client = factory.CreateClient();

        var llm = factory.Services.GetRequiredService<ScriptedLlmClient>();
        llm.Enqueue("Lucifer",
            "{\"thought\":\"Respond\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"Hello from WS\"}");

        await WaitForAgentAsync(factory.Services, "lucifer");

        var wsClient = factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers[OperationalAuthGuard.HeaderName] = "test-operator-key";
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        var request = JsonSerializer.Serialize(new { type = "task", toAgentId = "lucifer", content = "Say hello" });
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(request),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        var buffer = new byte[8 * 1024];

        while (DateTime.UtcNow < deadline)
        {
            var text = await ReceiveTextMessageAsync(socket, buffer);
            if (TryIsExpectedReport(text, expectedContentFragment: "Hello from WS"))
            {
                return;
            }
        }

        throw new TimeoutException("Did not receive expected Report over WebSocket");
    }

    private static async Task<string> ReceiveTextMessageAsync(WebSocket socket, byte[] buffer)
    {
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            result.MessageType.Should().Be(WebSocketMessageType.Text);
            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    private static bool TryIsExpectedReport(string payload, string expectedContentFragment)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("type", out var type))
            {
                return false;
            }

            if (!"Report".Equals(type.GetString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("content", out var content))
            {
                return false;
            }

            var contentText = content.GetString();
            return contentText is not null
                && contentText.Contains(expectedContentFragment, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
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
