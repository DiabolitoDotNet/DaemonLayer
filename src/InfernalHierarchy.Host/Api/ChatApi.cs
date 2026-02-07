using InfernalHierarchy.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class ChatApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/chat", async (
            ChatRequest request,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new { error = "Missing request body: message" });
            }

            if (request.Message.Length > 10_000)
            {
                return Results.BadRequest(new { error = "Message too long (max 10000 chars)" });
            }

            var toAgentId = string.IsNullOrWhiteSpace(request.ToAgentId)
                ? "lucifer"
                : request.ToAgentId.Trim();

            var timeoutMs = request.TimeoutMs is > 0 and <= 300_000
                ? request.TimeoutMs.Value
                : 180_000;

            var replyToId = $"http-{Guid.NewGuid():N}";
            var startedUtc = DateTime.UtcNow;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            var enumerator = messageBus.SubscribeAsync(replyToId, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

            try
            {
                var message = new AgentMessage
                {
                    FromAgentId = replyToId,
                    ToAgentId = toAgentId,
                    Type = MessageType.Task,
                    Content = request.Message,
                    Payload = new Dictionary<string, object>
                    {
                        ["transport"] = "http",
                        ["http_request_id"] = replyToId,
                        ["http_started_utc"] = startedUtc.ToString("O")
                    }
                };

                await messageBus.PublishAsync(message, ct);

                while (await enumerator.MoveNextAsync())
                {
                    var response = enumerator.Current;

                    // Prefer the agent's final report; ignore other message types if any.
                    if (response.Type != MessageType.Report)
                    {
                        continue;
                    }

                    return Results.Ok(new ChatResponse(
                        fromAgentId: response.FromAgentId,
                        toAgentId: response.ToAgentId,
                        content: response.Content,
                        payload: response.Payload,
                        receivedUtc: DateTime.UtcNow,
                        durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds));
                }

                return Results.Problem(
                    title: "Timeout",
                    detail: $"No report received within {timeoutMs}ms",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return Results.Problem(
                    title: "Timeout",
                    detail: $"No report received within {timeoutMs}ms",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            finally
            {
                await enumerator.DisposeAsync();
                if (messageBus is ChannelMessageBus cmb)
                {
                    cmb.CleanupAgent(replyToId);
                }
            }
        });
    }
}
