using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Telegram.Commands;
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

namespace InfernalHierarchy.Telegram;

/// <summary>
/// Telegram Bot service for receiving commands and sending responses
/// </summary>
public class TelegramBotService : BackgroundService
{
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

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
            };

            await _botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💀 Telegram bot failed to start");
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
                    FromAgentId = "telegram",
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
                await botClient.SendMessage(chatId, "✅ Task received by Lucifer...", cancellationToken: ct);
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

public class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;
    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();
}
