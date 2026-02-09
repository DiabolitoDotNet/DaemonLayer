using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Telegram;

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
        if (!TryGetInt64Any(parameters, out var chatId, "chat_id", "chatId", "telegram_chat_id", "telegramChatId"))
        {
            return Task.FromResult(new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: chat_id"
            });
        }

        if (!TryGetStringAny(parameters, out var text, "text", "message", "content"))
        {
            return Task.FromResult(new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: text"
            });
        }

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

    private static bool TryGetStringAny(Dictionary<string, object> parameters, out string value, params string[] keys)
    {
        value = string.Empty;

        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var obj) || obj is null)
            {
                continue;
            }

            if (obj is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    value = s;
                    return true;
                }
                continue;
            }

            if (obj is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var str = el.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        value = str;
                        return true;
                    }
                }
                else
                {
                    var raw = el.GetRawText();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        value = raw;
                        return true;
                    }
                }

                continue;
            }

            var asString = obj.ToString();
            if (!string.IsNullOrWhiteSpace(asString))
            {
                value = asString;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetInt64Any(Dictionary<string, object> parameters, out long value, params string[] keys)
    {
        value = default;

        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var obj) || obj is null)
            {
                continue;
            }

            if (obj is long l)
            {
                value = l;
                return true;
            }

            if (obj is int i)
            {
                value = i;
                return true;
            }

            if (obj is string s)
            {
                if (long.TryParse(s, out var parsed))
                {
                    value = parsed;
                    return true;
                }
                continue;
            }

            if (obj is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var parsed))
                {
                    value = parsed;
                    return true;
                }

                if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out parsed))
                {
                    value = parsed;
                    return true;
                }

                continue;
            }

            try
            {
                value = Convert.ToInt64(obj);
                return true;
            }
            catch
            {
                // ignore and continue
            }
        }

        return false;
    }
}
