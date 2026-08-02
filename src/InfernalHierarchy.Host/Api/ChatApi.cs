using InfernalHierarchy.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace InfernalHierarchy.Host.Api;

internal static class ChatApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapPost("/api/chat", async (
            HttpContext ctx,
            [FromBody] ChatRequest request,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

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

            var correlationId = ResolveCorrelationId(ctx);
            ctx.Response.Headers["X-Correlation-Id"] = correlationId;

            var timeoutMs = request.TimeoutMs is > 0 and <= 300_000
                ? request.TimeoutMs.Value
                : 180_000;
            var executionProfile = string.IsNullOrWhiteSpace(request.ExecutionProfile)
                ? "Research"
                : request.ExecutionProfile.Trim();

            var replyToId = $"http-{Guid.NewGuid():N}";
            var startedUtc = DateTime.UtcNow;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            var enumerator = messageBus.SubscribeAsync(replyToId, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

            try
            {
                var message = new AgentMessage
                {
                    Id = replyToId,
                    FromAgentId = replyToId,
                    ToAgentId = toAgentId,
                    Type = MessageType.Task,
                    Content = request.Message,
                    CorrelationId = correlationId,
                    Payload = new Dictionary<string, object>
                    {
                        ["transport"] = "http",
                        ["http_request_id"] = replyToId,
                        ["http_started_utc"] = startedUtc.ToString("O"),
                        ["correlation_id"] = correlationId,
                        ["execution_profile"] = executionProfile
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
                        correlationId: response.CorrelationId ?? correlationId,
                        causationId: response.CausationId,
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

    private static string ResolveCorrelationId(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var provided)
            && provided.Count == 1
            && !string.IsNullOrWhiteSpace(provided.ToString()))
        {
            return provided.ToString();
        }

        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(traceId)
            ? Guid.NewGuid().ToString("N")
            : traceId;
    }
}
