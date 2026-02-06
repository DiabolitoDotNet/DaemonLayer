using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Host.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Ui;

internal static class WebSocketInterface
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.WebIndented;

    public static void Map(WebApplication app)
    {
        app.MapGet("/ws", async (
            HttpContext context,
            IMessageBus messageBus,
            IOptions<WebSocketInterfaceOptions> optionsAccessor,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;

            if (!options.Enabled)
            {
                return Results.NotFound();
            }

            if (options.LocalOnly && !LoopbackGuard.IsLoopback(context.Connection.RemoteIpAddress))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest(new { error = "Expected WebSocket request" });
            }

            var logger = loggerFactory.CreateLogger("WebSocketInterface");
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = $"ws-{Guid.NewGuid():N}";

            logger.LogInformation("WS connected: {ConnectionId} from {Remote}", connectionId, context.Connection.RemoteIpAddress);

            // Subscribe to both broadcast messages and the connection inbox.
            // Clients can send tasks with fromAgentId=connectionId; agents reply to that.
            var sendSemaphore = new SemaphoreSlim(1, 1);

            var broadcastTask = Task.Run(() => PumpAsync(
                source: messageBus.SubscribeToBroadcastsAsync(ct),
                socket: socket,
                sendSemaphore: sendSemaphore,
                prefix: "broadcast",
                ct: ct), ct);

            var inboxTask = Task.Run(() => PumpAsync(
                source: messageBus.SubscribeAsync(connectionId, ct),
                socket: socket,
                sendSemaphore: sendSemaphore,
                prefix: "inbox",
                ct: ct), ct);

            var receiveTask = Task.Run(() => ReceiveLoopAsync(
                socket: socket,
                connectionId: connectionId,
                messageBus: messageBus,
                maxBytes: options.MaxClientMessageBytes,
                logger: logger,
                ct: ct), ct);

            try
            {
                await Task.WhenAny(broadcastTask, inboxTask, receiveTask);
            }
            finally
            {
                try
                {
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                catch
                {
                    // Best-effort.
                }

                if (messageBus is InfernalHierarchy.Messaging.Bus.ChannelMessageBus cmb)
                {
                    cmb.CleanupAgent(connectionId);
                }

                logger.LogInformation("WS disconnected: {ConnectionId}", connectionId);
            }

            return Results.Empty;
        });
    }

    private static async Task PumpAsync(
        IAsyncEnumerable<AgentMessage> source,
        WebSocket socket,
        SemaphoreSlim sendSemaphore,
        string prefix,
        CancellationToken ct)
    {
        await foreach (var msg in source.WithCancellation(ct))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var payload = new
            {
                kind = "agent_message",
                stream = prefix,
                id = msg.Id,
                from = msg.FromAgentId,
                to = msg.ToAgentId,
                type = msg.Type.ToString(),
                content = msg.Content,
                receivedUtc = DateTime.UtcNow,
                payload = SanitizePayload(msg.Payload)
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await sendSemaphore.WaitAsync(ct);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: ct);
            }
            finally
            {
                sendSemaphore.Release();
            }
        }
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        string connectionId,
        IMessageBus messageBus,
        int maxBytes,
        ILogger logger,
        CancellationToken ct)
    {
        var buffer = new byte[Math.Min(maxBytes, 64 * 1024)];
        using var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult? result = null;

            do
            {
                result = await socket.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.Count > 0)
                {
                    if (ms.Length + result.Count > maxBytes)
                    {
                        logger.LogWarning("WS client message exceeded maxBytes={MaxBytes}", maxBytes);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
            }
            while (!result.EndOfMessage);

            var text = Encoding.UTF8.GetString(ms.ToArray());
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            WsClientMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<WsClientMessage>(text, JsonOptions);
            }
            catch
            {
                logger.LogWarning("WS invalid JSON received");
                continue;
            }

            if (msg is null)
            {
                continue;
            }

            if (string.Equals(msg.Type, "ping", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(msg.Type, "task", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(msg.Type, "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var toAgentId = string.IsNullOrWhiteSpace(msg.ToAgentId) ? "lucifer" : msg.ToAgentId.Trim();
            var content = msg.Content ?? string.Empty;

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var agentMessage = new AgentMessage
            {
                FromAgentId = connectionId,
                ToAgentId = toAgentId,
                Type = MessageType.Task,
                Content = content,
                Payload = new Dictionary<string, object>
                {
                    ["transport"] = "websocket",
                    ["ws_connection_id"] = connectionId
                }
            };

            await messageBus.PublishAsync(agentMessage, ct);
        }
    }

    private static Dictionary<string, object?> SanitizePayload(Dictionary<string, object> payload)
    {
        if (payload.Count == 0) return new Dictionary<string, object?>();

        var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in payload)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;

            safe[k] = v switch
            {
                null => null,
                string s => s,
                bool b => b,
                int i => i,
                long l => l,
                double d => d,
                DateTime dt => dt,
                _ => v.ToString()
            };
        }

        return safe;
    }

    private sealed record WsClientMessage(string Type, string? ToAgentId, string? Content);
}
