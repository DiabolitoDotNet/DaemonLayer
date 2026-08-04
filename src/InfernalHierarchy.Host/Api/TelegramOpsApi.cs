using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Telegram.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InfernalHierarchy.Host.Api;

internal static class TelegramOpsApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        var telegramOptions = app.Services.GetService<IOptions<TelegramOptions>>()?.Value;

        app.MapPost("/api/ops/telegram/simulate-inbound", async (
            HttpContext ctx,
            [FromBody] TelegramSimulateInboundRequest request,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "Missing request body: text" });
            }

            var chatId = request.ChatId is not null && request.ChatId.Value != 0
                ? request.ChatId.Value
                : ResolveDefaultTelegramChatId(telegramOptions);
            if (chatId == 0)
            {
                return Results.BadRequest(new { error = "Unable to resolve Telegram chat id. Provide chatId or configure Telegram defaults." });
            }

            var userId = request.UserId is not null && request.UserId.Value != 0
                ? request.UserId.Value
                : ResolveDefaultTelegramUserId(telegramOptions);

            var toAgentId = string.IsNullOrWhiteSpace(request.ToAgentId)
                ? "lucifer"
                : request.ToAgentId.Trim();

            var message = new AgentMessage
            {
                Id = $"telegram-sim-{Guid.NewGuid():N}",
                FromAgentId = "telegram",
                ToAgentId = toAgentId,
                Type = MessageType.Task,
                Content = BuildLuciferTaskContent(request.Text.Trim(), telegramOptions?.LuciferPreamble),
                Payload = new Dictionary<string, object>
                {
                    ["transport"] = "telegram-simulated",
                    ["telegram_chat_id"] = chatId,
                    ["telegram_user_id"] = userId,
                    ["simulated"] = true
                }
            };

            await messageBus.PublishAsync(message, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                queued = true,
                messageId = message.Id,
                fromAgentId = message.FromAgentId,
                toAgentId = message.ToAgentId,
                telegramChatId = chatId,
                telegramUserId = userId
            });
        });

        app.MapPost("/api/ops/telegram/simulate-command", async (
            HttpContext ctx,
            [FromBody] TelegramSimulateCommandRequest request,
            ITelegramInboundSimulator simulator,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return Results.BadRequest(new { error = "Missing request body: command" });
            }

            var command = request.Command.Trim();
            if (!command.StartsWith('/'))
            {
                command = "/" + command;
            }

            var chatId = request.ChatId is not null && request.ChatId.Value != 0
                ? request.ChatId.Value
                : ResolveDefaultTelegramChatId(telegramOptions);
            if (chatId == 0)
            {
                return Results.BadRequest(new { error = "Unable to resolve Telegram chat id. Provide chatId or configure Telegram defaults." });
            }

            var userId = request.UserId is not null && request.UserId.Value != 0
                ? request.UserId.Value
                : ResolveDefaultTelegramUserId(telegramOptions);

            await simulator.SimulateInboundTextAsync(chatId, userId, command, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                accepted = true,
                command,
                chatId,
                userId
            });
        });
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

    private static long ResolveDefaultTelegramUserId(TelegramOptions? options)
    {
        if (options is null || options.AllowedUserIds.Length == 0)
        {
            return 0;
        }

        return options.AllowedUserIds[0];
    }

    private static string BuildLuciferTaskContent(string userText, string? preamble)
    {
        if (string.IsNullOrWhiteSpace(preamble))
        {
            return userText;
        }

        return $"{preamble.Trim()}\n\n---\nDemande utilisateur (Telegram):\n{userText}";
    }
}

public sealed record TelegramSimulateInboundRequest(
    string Text,
    long? ChatId = null,
    long? UserId = null,
    string? ToAgentId = null);

public sealed record TelegramSimulateCommandRequest(
    string Command,
    long? ChatId = null,
    long? UserId = null);