using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Telegram;

/// <summary>
/// Tool for sending messages to Telegram
/// </summary>
public class TelegramSendTool : ITool
{
    private readonly ILogger<TelegramSendTool> _logger;
    private readonly ITelegramMessageSender _sender;

    public string Name => "send_telegram";
    public string Description => "Send a message to Telegram user. Requires: chat_id, text";

    public TelegramSendTool(ILogger<TelegramSendTool> logger, ITelegramMessageSender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!TryGetInt64Any(parameters, out var chatId, "chat_id", "chatId", "telegram_chat_id", "telegramChatId"))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: chat_id"
            };
        }

        if (!TryGetStringAny(parameters, out var text, "text", "message", "content"))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: text"
            };
        }

        var sendResult = await _sender.SendMessageAsync(chatId, text, ct).ConfigureAwait(false);
        if (!sendResult.Success)
        {
            _logger.LogWarning(
                "Telegram send failed for chat {ChatId} (retryable={Retryable}): {Error}",
                chatId,
                sendResult.Retryable,
                sendResult.Error);

            return new ToolResult
            {
                Success = false,
                Error = sendResult.Error ?? "Unknown Telegram send error",
                Metadata = new Dictionary<string, object>
                {
                    ["chat_id"] = chatId,
                    ["retryable"] = sendResult.Retryable,
                    ["latency_ms"] = sendResult.LatencyMs,
                }
            };
        }

        _logger.LogInformation("📤 Telegram message sent: ChatId={ChatId}, MessageId={MessageId}", chatId, sendResult.MessageId);

        return new ToolResult
        {
            Success = true,
            Output = $"Message sent to Telegram chat {chatId}",
            Metadata = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["message_id"] = sendResult.MessageId ?? 0,
                ["latency_ms"] = sendResult.LatencyMs,
                ["text_length"] = text.Length,
                ["delivery_status"] = "sent"
            }
        };
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
