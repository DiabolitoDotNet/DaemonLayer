using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace InfernalHierarchy.Telegram.Commands;

public sealed record TelegramCommandContext(
    ITelegramBotClient BotClient,
    long ChatId,
    string RawText,
    string[] Parts,
    IMessageBus MessageBus,
    ILogger Logger);
