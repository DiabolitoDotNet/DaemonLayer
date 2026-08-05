using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Telegram.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace InfernalHierarchy.Host.Api;

internal static class ChatApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        var telegramOptions = app.Services.GetService<IOptions<TelegramOptions>>()?.Value;
        var executionProfilesOptions = app.Services.GetService<IOptions<ExecutionProfilesOptions>>()?.Value;
        var defaultExecutionProfile = string.IsNullOrWhiteSpace(executionProfilesOptions?.DefaultProfile)
            ? "Research"
            : executionProfilesOptions.DefaultProfile.Trim();

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
                ? defaultExecutionProfile
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

                var telegramChatId = request.TelegramChatId.HasValue && request.TelegramChatId.Value != 0
                    ? request.TelegramChatId.Value
                    : ResolveDefaultTelegramChatId(telegramOptions);

                if (telegramChatId != 0)
                {
                    message.Payload["telegram_chat_id"] = telegramChatId;
                }

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
                        payload: AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload(response.Content, response.Payload),
                        correlationId: response.CorrelationId ?? correlationId,
                        causationId: response.CausationId,
                        receivedUtc: DateTime.UtcNow,
                        durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds));
                }

                return Results.Json(
                    new ChatResponse(
                        fromAgentId: "system",
                        toAgentId: toAgentId,
                        content: $"Timeout: no report received within {timeoutMs}ms",
                        payload: AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload(),
                        correlationId: correlationId,
                        causationId: null,
                        receivedUtc: DateTime.UtcNow,
                        durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return Results.Json(
                    new ChatResponse(
                        fromAgentId: "system",
                        toAgentId: toAgentId,
                        content: $"Timeout: no report received within {timeoutMs}ms",
                        payload: AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload(),
                        correlationId: correlationId,
                        causationId: null,
                        receivedUtc: DateTime.UtcNow,
                        durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds),
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

    private static long ResolveDefaultTelegramChatId(TelegramOptions? options)
    {
        if (options is null)
        {
            return 0;
        }

        if (options.StartupNotificationChatId != 0)
        {
            return options.StartupNotificationChatId;
        }

        return options.AllowedUserIds.Length == 1
            ? options.AllowedUserIds[0]
            : 0;
    }
}
