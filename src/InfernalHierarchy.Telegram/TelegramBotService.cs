using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace InfernalHierarchy.Telegram;

/// <summary>
/// Telegram Bot service for receiving commands and sending responses
/// </summary>
public class TelegramBotService : BackgroundService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IMessageBus _messageBus;
    private readonly TelegramOptions _options;
    private ITelegramBotClient? _botClient;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        IMessageBus messageBus,
        ILogger<TelegramBotService> logger)
    {
        _options = options.Value;
        _messageBus = messageBus;
        _logger = logger;
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

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message || message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        // Check if user is allowed
        if (_options.AllowedUserIds.Length > 0 && !_options.AllowedUserIds.Contains(userId))
        {
            _logger.LogWarning("🚫 Unauthorized user {UserId} attempted to send: {Text}", userId, messageText);
            await botClient.SendMessage(chatId, "❌ You are not authorized to use this bot.", cancellationToken: ct);
            return;
        }

        _logger.LogInformation("📩 Telegram message from {UserId}: {Text}", userId, messageText);

        try
        {
            // Handle commands
            if (messageText.StartsWith('/'))
            {
                await HandleCommandAsync(botClient, chatId, messageText, ct);
            }
            else
            {
                // Send as task to main agent
                var agentMessage = new Core.Entities.AgentMessage
                {
                    FromAgentId = "telegram",
                    ToAgentId = "lucifer", // Main agent
                    Type = Core.Entities.MessageType.Task,
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
            _logger.LogError(ex, "Failed to handle Telegram message");
            await botClient.SendMessage(chatId, $"❌ Error: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task HandleCommandAsync(ITelegramBotClient botClient, long chatId, string command, CancellationToken ct)
    {
        try
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "/start":
                    await botClient.SendMessage(chatId,
                        "🔥 **Welcome to the Infernal Hierarchy!**\n\n" +
                        "I am the gateway to a system of demon agents organized in a hierarchy.\n\n" +
                        "Send me any task and I'll delegate it to Lucifer, the Supreme Agent.\n\n" +
                        "Use /help to see available commands.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct);
                    break;

                case "/help":
                    await botClient.SendMessage(chatId,
                        "📚 **Available Commands:**\n\n" +
                        "**Basic:**\n" +
                        "/start - Initialize the bot\n" +
                        "/help - Show this help message\n" +
                        "/status - Check hierarchy status\n\n" +
                        "**Agent Management:**\n" +
                        "/summon <demon> <rank> - Create a new agent\n" +
                        "  Example: `/summon Paimon duke`\n" +
                        "/kill <agent_id> - Terminate an agent\n\n" +
                        "**Memory:**\n" +
                        "/memory [query] - Search shared memory\n" +
                        "/memory facts - List recent facts\n" +
                        "/memory decisions - List recent decisions\n" +
                        "/memory tasks - List active tasks\n\n" +
                        "**Task Delegation:**\n" +
                        "Just send a regular message to delegate a task to Lucifer!",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct);
                    break;

                case "/status":
                    await HandleStatusCommandAsync(botClient, chatId, ct);
                    break;

                case "/summon":
                    await HandleSummonCommandAsync(botClient, chatId, parts, ct);
                    break;

                case "/kill":
                    await HandleKillCommandAsync(botClient, chatId, parts, ct);
                    break;

                case "/memory":
                    await HandleMemoryCommandAsync(botClient, chatId, parts, ct);
                    break;

                default:
                    await botClient.SendMessage(chatId,
                        "❓ Unknown command. Use /help for available commands.",
                        cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command: {Command}", command);
            await botClient.SendMessage(chatId,
                $"❌ Error executing command: {ex.Message}",
                cancellationToken: ct);
        }
    }

    private async Task HandleStatusCommandAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        // Request status from Lucifer via message bus
        var statusRequest = new Core.Entities.AgentMessage
        {
            FromAgentId = "telegram",
            ToAgentId = "lucifer",
            Type = Core.Entities.MessageType.Query,
            Content = "status",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = chatId,
                ["command"] = "status"
            }
        };

        await _messageBus.PublishAsync(statusRequest, ct);
        await botClient.SendMessage(chatId, "📊 Querying hierarchy status...", cancellationToken: ct);
    }

    private async Task HandleSummonCommandAsync(ITelegramBotClient botClient, long chatId, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await botClient.SendMessage(chatId,
                "❌ Usage: `/summon <demon_name> <rank>`\n" +
                "Example: `/summon Paimon duke`\n\n" +
                "Available ranks: supreme, prince, duke, worker",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        var demonName = parts[1];
        var rank = parts[2].ToLower();

        // Validate rank
        if (!Enum.TryParse<Core.Entities.AgentRank>(rank, ignoreCase: true, out var agentRank))
        {
            await botClient.SendMessage(chatId,
                $"❌ Invalid rank: {rank}\n" +
                "Available ranks: supreme, prince, duke, worker",
                cancellationToken: ct);
            return;
        }

        // Send creation request to Lucifer
        var summonRequest = new Core.Entities.AgentMessage
        {
            FromAgentId = "telegram",
            ToAgentId = "lucifer",
            Type = Core.Entities.MessageType.Command,
            Content = $"create_sub_agent",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = chatId,
                ["demon_name"] = demonName,
                ["rank"] = agentRank.ToString(),
                ["command"] = "summon"
            }
        };

        await _messageBus.PublishAsync(summonRequest, ct);
        await botClient.SendMessage(chatId,
            $"🔨 Summoning {demonName} ({agentRank})...",
            cancellationToken: ct);
    }

    private async Task HandleKillCommandAsync(ITelegramBotClient botClient, long chatId, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            await botClient.SendMessage(chatId,
                "❌ Usage: `/kill <agent_id>`\n" +
                "Use /status to see active agent IDs",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        var agentId = parts[1];

        // Send termination request
        var killRequest = new Core.Entities.AgentMessage
        {
            FromAgentId = "telegram",
            ToAgentId = agentId,
            Type = Core.Entities.MessageType.Command,
            Content = "terminate",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = chatId,
                ["command"] = "kill"
            }
        };

        await _messageBus.PublishAsync(killRequest, ct);
        await botClient.SendMessage(chatId,
            $"💀 Sending termination command to agent {agentId}...",
            cancellationToken: ct);
    }

    private async Task HandleMemoryCommandAsync(ITelegramBotClient botClient, long chatId, string[] parts, CancellationToken ct)
    {
        var query = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

        // Send memory query to Lucifer
        var memoryRequest = new Core.Entities.AgentMessage
        {
            FromAgentId = "telegram",
            ToAgentId = "lucifer",
            Type = Core.Entities.MessageType.Query,
            Content = "read_memory",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = chatId,
                ["query"] = query,
                ["command"] = "memory"
            }
        };

        await _messageBus.PublishAsync(memoryRequest, ct);
        await botClient.SendMessage(chatId,
            $"🧠 Querying shared memory{(string.IsNullOrEmpty(query) ? "" : $": {query}")}...",
            cancellationToken: ct);
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiException => $"Telegram API Error:\n[{apiException.ErrorCode}]\n{apiException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError(exception, "Telegram polling error: {ErrorMessage}", errorMessage);
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
