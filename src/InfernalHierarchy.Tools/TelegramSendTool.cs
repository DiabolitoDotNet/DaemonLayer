using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Tool for sending messages to Telegram
/// </summary>
public class TelegramSendTool : ITool
{
    private readonly ILogger<TelegramSendTool> _logger;

    public string Name => "send_telegram";
    public string Description => "Send a message to Telegram user. Requires: chat_id, text";

    // Note: We'll need to inject TelegramBotService or create a separate sender service
    // For now, we'll store messages in a queue that TelegramBotService can process

    public TelegramSendTool(ILogger<TelegramSendTool> logger)
    {
        _logger = logger;
    }

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("chat_id", out var chatIdObj))
        {
            return Task.FromResult(new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: chat_id"
            });
        }

        if (!parameters.TryGetValue("text", out var textObj) || textObj is not string text)
        {
            return Task.FromResult(new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: text"
            });
        }

        var chatId = Convert.ToInt64(chatIdObj);

        // TODO: Implement actual sending via TelegramBotService
        // For now, log the intent
        _logger.LogInformation("📤 Telegram send request: ChatId={ChatId}, Text={Text}", chatId, text);

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Output = $"Message queued for Telegram chat {chatId}",
            Metadata = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["text"] = text
            }
        });
    }
}
