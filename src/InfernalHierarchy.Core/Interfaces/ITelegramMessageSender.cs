namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Abstraction for sending Telegram messages from tools without coupling to transport implementation.
/// </summary>
public interface ITelegramMessageSender
{
    Task<TelegramSendResult> SendMessageAsync(long chatId, string text, CancellationToken ct = default);
}

public sealed class TelegramSendResult
{
    public bool Success { get; init; }

    public long ChatId { get; init; }

    public int? MessageId { get; init; }

    public string? Error { get; init; }

    public bool Retryable { get; init; }

    public double LatencyMs { get; init; }

    public static TelegramSendResult Ok(long chatId, int? messageId, TimeSpan latency)
        => new()
        {
            Success = true,
            ChatId = chatId,
            MessageId = messageId,
            LatencyMs = latency.TotalMilliseconds,
        };

    public static TelegramSendResult Fail(long chatId, string error, bool retryable, TimeSpan latency)
        => new()
        {
            Success = false,
            ChatId = chatId,
            Error = error,
            Retryable = retryable,
            LatencyMs = latency.TotalMilliseconds,
        };
}
