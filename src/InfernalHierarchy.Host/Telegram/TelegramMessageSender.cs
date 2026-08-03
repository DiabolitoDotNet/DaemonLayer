using System.Diagnostics;
using InfernalHierarchy.Core.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace InfernalHierarchy.Host.Telegram;

public sealed class TelegramMessageSender : ITelegramMessageSender
{
    private readonly TelegramBotClientFactory _factory;
    private readonly IOptions<TelegramOptions> _options;
    private readonly ILogger<TelegramMessageSender> _logger;

    public TelegramMessageSender(
        TelegramBotClientFactory factory,
        IOptions<TelegramOptions> options,
        ILogger<TelegramMessageSender> logger)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    public async Task<TelegramSendResult> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (chatId == 0)
        {
            return TelegramSendResult.Fail(chatId, "Invalid chat_id (0)", retryable: false, TimeSpan.Zero);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return TelegramSendResult.Fail(chatId, "Message text is empty", retryable: false, TimeSpan.Zero);
        }

        var botToken = _options.Value.BotToken;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return TelegramSendResult.Fail(chatId, "Telegram bot token is not configured", retryable: false, TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            ITelegramBotClient client = _factory.GetOrCreateClient(botToken);
            var sent = await client.SendMessage(chatId, text, cancellationToken: ct).ConfigureAwait(false);
            sw.Stop();

            return TelegramSendResult.Ok(chatId, sent.Id, sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            return TelegramSendResult.Fail(chatId, "Telegram send cancelled", retryable: true, sw.Elapsed);
        }
        catch (ApiRequestException ex)
        {
            sw.Stop();
            var retryable = ex.ErrorCode == 429 || ex.ErrorCode >= 500;
            _logger.LogWarning(ex, "Telegram API send failed for chat {ChatId} (retryable={Retryable})", chatId, retryable);
            return TelegramSendResult.Fail(chatId, ex.Message, retryable, sw.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Telegram transport send failed for chat {ChatId}", chatId);
            return TelegramSendResult.Fail(chatId, ex.Message, retryable: true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected Telegram send error for chat {ChatId}", chatId);
            return TelegramSendResult.Fail(chatId, ex.Message, retryable: false, sw.Elapsed);
        }
    }
}
