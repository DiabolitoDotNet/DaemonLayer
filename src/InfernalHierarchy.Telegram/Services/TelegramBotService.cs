using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Telegram.Commands;
using InfernalHierarchy.Telegram.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using CoreMessageType = InfernalHierarchy.Core.Entities.MessageType;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Telegram.Services;

/// <summary>
/// Telegram Bot service for receiving commands and sending responses
/// </summary>
public class TelegramBotService : BackgroundService
{
    private const string TelegramAgentId = "telegram";
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IMessageBus _messageBus;
    private readonly TelegramOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, ITelegramCommandHandler> _commandHandlers;
    private ITelegramBotClient? _botClient;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        IMessageBus messageBus,
        ILogger<TelegramBotService> logger,
        IServiceProvider serviceProvider,
        IEnumerable<ITelegramCommandHandler>? commandHandlers = null)
    {
        _options = options.Value;
        _messageBus = messageBus;
        _logger = logger;
        _serviceProvider = serviceProvider;

        var handlers = (commandHandlers?.ToList() ?? DefaultCommandHandlers.CreateAll()).ToList();
        _commandHandlers = handlers
            .GroupBy(h => h.Command, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogWarning("⚠️ Telegram bot token not configured. Telegram service disabled.");
            return;
        }

        _botClient = new TelegramBotClient(_options.BotToken);

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("🤖 Telegram bot started: @{Username}", me.Username);

            // Start a background listener that forwards agent reports back to Telegram.
            // Agents reply to the sender via message bus (ToAgentId = task.FromAgentId).
            // We publish tasks with FromAgentId = "telegram", so listening as "telegram" lets us
            // receive the final Report (and forward it to the originating chat).
            var forwarderTask = Task.Run(() => ForwardAgentMessagesToTelegramAsync(stoppingToken), stoppingToken);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
            };

            await _botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);

            // ReceiveAsync completes on cancellation/error; ensure forwarder is also awaited.
            await forwarderTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💀 Telegram bot failed to start");
        }
    }

    private async Task ForwardAgentMessagesToTelegramAsync(CancellationToken ct)
    {
        if (_botClient is null)
        {
            return;
        }

        try
        {
            await foreach (var message in _messageBus.SubscribeAsync(TelegramAgentId, ct))
            {
                if (message.Type is not (CoreMessageType.Report or CoreMessageType.Notification or CoreMessageType.ToolResult))
                {
                    continue;
                }

                if (!TryGetTelegramChatId(message, out var chatId))
                {
                    _logger.LogDebug(
                        "Skipping agent message {MessageId} from {From}: missing telegram_chat_id payload",
                        message.Id,
                        message.FromAgentId);
                    continue;
                }

                var text = string.IsNullOrWhiteSpace(message.Content)
                    ? $"(empty {message.Type} from {message.FromAgentId})"
                    : FormatForTelegram(message);

                // Keep Telegram messages within reasonable size.
                if (text.Length > 3800)
                {
                    text = text[..3800] + "…";
                }

                try
                {
                    await _botClient.SendMessage(chatId, text, cancellationToken: ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to forward agent message {MessageId} from {From} to Telegram chat {ChatId}",
                        message.Id,
                        message.FromAgentId,
                        chatId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram forwarder loop failed");
        }
    }

    private static string FormatForTelegram(AgentMessage message)
    {
        var text = message.Content ?? string.Empty;

        // Special-case: common success path for email tool.
        // Example: "Task E2E-EMAIL-3 completed: Email successfully sent to ... Telegram→Lucifer→SMTP workflow is operational."
        if (text.Contains("Email successfully sent", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Email sent", StringComparison.OrdinalIgnoreCase))
        {
            var email = TryExtractEmail(text);
            var masked = MaskEmail(email);
            var summary = string.IsNullOrWhiteSpace(email)
                ? "✅ Email sent"
                : $"✅ Email sent to {masked}";

            return summary;
        }

        // Generic: strip noisy prefix "Task X completed:" if present.
        var idx = text.IndexOf(" completed:", StringComparison.OrdinalIgnoreCase);
        if (text.StartsWith("Task ", StringComparison.OrdinalIgnoreCase) && idx > 0)
        {
            var after = text[(idx + " completed:".Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(after))
            {
                text = after;
            }
        }

        // Generic: keep it short (first sentence) for readability.
        var firstSentenceEnd = text.IndexOf('.', StringComparison.Ordinal);
        if (firstSentenceEnd > 0 && firstSentenceEnd < 240)
        {
            text = text[..(firstSentenceEnd + 1)];
        }

        return text.Trim();
    }

    private static string? TryExtractEmail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Simple, pragmatic email extractor.
        var m = Regex.Match(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        return m.Success ? m.Value : null;
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return email;

        var local = email[..at];
        var domain = email[(at + 1)..];

        var visible = Math.Min(2, local.Length);
        var maskedLocal = visible == local.Length
            ? local
            : local[..visible] + new string('*', Math.Max(3, local.Length - visible));

        return $"{maskedLocal}@{domain}";
    }

    private static bool TryGetTelegramChatId(AgentMessage message, out long chatId)
    {
        chatId = default;

        if (!message.Payload.TryGetValue("telegram_chat_id", out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            chatId = raw switch
            {
                long l => l,
                int i => i,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToInt64(raw)
            };

            return chatId != 0;
        }
        catch
        {
            return false;
        }
    }

    internal async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message || message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        if (_options.AllowedUserIds.Length > 0 && !_options.AllowedUserIds.Contains(userId))
        {
            _logger.LogWarning("🚫 Unauthorized user {UserId} attempted to send: {Text}", userId, messageText);
            await botClient.SendMessage(chatId, "❌ You are not authorized to use this bot.", cancellationToken: ct);
            return;
        }

        _logger.LogInformation("📩 Telegram message from {UserId}: {Text}", userId, messageText);

        try
        {
            if (messageText.StartsWith('/'))
            {
                await HandleCommandAsync(botClient, chatId, messageText, ct);
            }
            else
            {
                var agentMessage = new AgentMessage
                {
                    FromAgentId = TelegramAgentId,
                    ToAgentId = "lucifer",
                    Type = CoreMessageType.Task,
                    Content = messageText,
                    Payload = new Dictionary<string, object>
                    {
                        ["telegram_chat_id"] = chatId,
                        ["telegram_user_id"] = userId
                    }
                };

                await _messageBus.PublishAsync(agentMessage, ct);
                await botClient.SendMessage(chatId, "✅ Task queued for Lucifer (processing soon)...", cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();
            var userMessage = "❌ An error occurred processing your request.";

            if (exceptionHandler != null)
            {
                var handlingResult = await exceptionHandler.HandleExceptionAsync(ex, $"TelegramMessage_{userId}");

                userMessage = handlingResult.ShouldRetry
                    ? $"⚠️ {handlingResult.Message} (will retry automatically)"
                    : $"❌ {handlingResult.Message}";

                _logger.LogError(
                    ex,
                    "🔥 Failed to handle Telegram message | Category: {Category} | CorrelationId: {CorrelationId}",
                    handlingResult.Category,
                    handlingResult.CorrelationId);
            }
            else
            {
                _logger.LogError(ex, "Failed to handle Telegram message");
                userMessage = $"❌ Error: {ex.Message}";
            }

            await botClient.SendMessage(chatId, userMessage, cancellationToken: ct);
        }
    }

    private async Task HandleCommandAsync(ITelegramBotClient botClient, long chatId, string command, CancellationToken ct)
    {
        try
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var cmd = parts[0];

            if (!_commandHandlers.TryGetValue(cmd, out var handler))
            {
                await botClient.SendMessage(chatId,
                    "❓ Unknown command. Use /help for available commands.",
                    cancellationToken: ct);
                return;
            }

            var context = new TelegramCommandContext(
                BotClient: botClient,
                ChatId: chatId,
                RawText: command,
                Parts: parts,
                MessageBus: _messageBus,
                Logger: _logger);

            await handler.HandleAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command: {Command}", command);
            await botClient.SendMessage(chatId,
                $"❌ Error executing command: {ex.Message}",
                cancellationToken: ct);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();

        if (exceptionHandler != null)
        {
            var handlingResult = exceptionHandler.HandleExceptionAsync(
                exception,
                "TelegramPolling").GetAwaiter().GetResult();

            _logger.LogError(
                exception,
                "🔥 Telegram polling error | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                handlingResult.Category,
                handlingResult.ShouldRetry,
                handlingResult.CorrelationId);
        }
        else
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiException => $"Telegram API Error:\n[{apiException.ErrorCode}]\n{apiException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(exception, "Telegram polling error: {ErrorMessage}", errorMessage);
        }

        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (_botClient == null)
        {
            _logger.LogWarning("Cannot send message: Telegram bot not initialized");
            return;
        }

        try
        {
            await _botClient.SendMessage(chatId, text, cancellationToken: ct);
            _logger.LogDebug("📤 Sent Telegram message to {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram message to {ChatId}", chatId);
        }
    }
}
