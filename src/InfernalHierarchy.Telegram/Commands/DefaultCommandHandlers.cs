using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using CoreMessageType = InfernalHierarchy.Core.Entities.MessageType;

namespace InfernalHierarchy.Telegram.Commands;

internal static class DefaultCommandHandlers
{
    public static IReadOnlyList<ITelegramCommandHandler> CreateAll() => new List<ITelegramCommandHandler>
    {
        new StartCommandHandler(),
        new HelpCommandHandler(),
        new StatusCommandHandler(),
        new SummonCommandHandler(),
        new KillCommandHandler(),
        new MemoryCommandHandler(),
        new UsageCommandHandler(),
        new LearningCommandHandler(),
        new ModelsCommandHandler(),
        new SuspendCommandHandler(),
        new ResumeCommandHandler()
    };

    internal sealed class StartCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/start";

        public Task HandleAsync(TelegramCommandContext context, CancellationToken ct) =>
            context.BotClient.SendMessage(
                context.ChatId,
                "🔥 **Welcome to the Infernal Hierarchy!**\n\n" +
                "I am the gateway to a system of demon agents organized in a hierarchy.\n\n" +
                "Send me any task and I'll delegate it to Lucifer, the Supreme Agent.\n\n" +
                "Use /help to see available commands.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
    }

    internal sealed class HelpCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/help";

        public Task HandleAsync(TelegramCommandContext context, CancellationToken ct) =>
            context.BotClient.SendMessage(
                context.ChatId,
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
                "**Learning & Stats:**\n" +
                "/usage - Show LLM token usage statistics\n" +
                "/learning [agent_id] - Show agent learning stats\n" +
                "/models - Show available LLM models\n\n" +
                "**Agent Control:**\n" +
                "/suspend <agent_id> - Suspend (hibernate) an agent\n" +
                "/resume <agent_id> - Resume a suspended agent\n\n" +
                "**Task Delegation:**\n" +
                "Just send a regular message to delegate a task to Lucifer!",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
    }

    internal sealed class StatusCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/status";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            var statusRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Query,
                Content = "status",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "status"
                }
            };

            await context.MessageBus.PublishAsync(statusRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, "📊 Querying hierarchy status...", cancellationToken: ct);
        }
    }

    internal sealed class SummonCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/summon";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            if (context.Parts.Length < 3)
            {
                await context.BotClient.SendMessage(
                    context.ChatId,
                    "❌ Usage: `/summon <demon_name> <rank>`\n" +
                    "Example: `/summon Paimon duke`\n\n" +
                    "Available ranks: supreme, prince, duke, worker",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }

            var demonName = context.Parts[1];
            var rank = context.Parts[2];

            if (!Enum.TryParse<AgentRank>(rank, ignoreCase: true, out var agentRank))
            {
                await context.BotClient.SendMessage(
                    context.ChatId,
                    $"❌ Invalid rank: {rank}\n" +
                    "Available ranks: supreme, prince, duke, worker",
                    cancellationToken: ct);
                return;
            }

            var summonRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Command,
                Content = "create_sub_agent",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["demon_name"] = demonName,
                    ["rank"] = agentRank.ToString(),
                    ["command"] = "summon"
                }
            };

            await context.MessageBus.PublishAsync(summonRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, $"🔨 Summoning {demonName} ({agentRank})...", cancellationToken: ct);
        }
    }

    internal sealed class KillCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/kill";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            if (context.Parts.Length < 2)
            {
                await context.BotClient.SendMessage(
                    context.ChatId,
                    "❌ Usage: `/kill <agent_id>`\n" +
                    "Use /status to see active agent IDs",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }

            var agentId = context.Parts[1];

            var killRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = agentId,
                Type = CoreMessageType.Command,
                Content = "terminate",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "kill"
                }
            };

            await context.MessageBus.PublishAsync(killRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, $"💀 Sending termination command to agent {agentId}...", cancellationToken: ct);
        }
    }

    internal sealed class MemoryCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/memory";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            var query = context.Parts.Length > 1 ? string.Join(" ", context.Parts.Skip(1)) : string.Empty;

            var memoryRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Query,
                Content = "read_memory",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["query"] = query,
                    ["command"] = "memory"
                }
            };

            await context.MessageBus.PublishAsync(memoryRequest, ct);
            await context.BotClient.SendMessage(
                context.ChatId,
                $"🧠 Querying shared memory{(string.IsNullOrEmpty(query) ? "" : $": {query}")}...",
                cancellationToken: ct);
        }
    }

    internal sealed class UsageCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/usage";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            var usageRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Query,
                Content = "token_usage",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "usage"
                }
            };

            await context.MessageBus.PublishAsync(usageRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, "📊 Fetching LLM token usage statistics...", cancellationToken: ct);
        }
    }

    internal sealed class LearningCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/learning";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            var agentId = context.Parts.Length > 1 ? context.Parts[1] : string.Empty;

            var learningRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Query,
                Content = "learning_stats",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["agent_id"] = agentId,
                    ["command"] = "learning"
                }
            };

            await context.MessageBus.PublishAsync(learningRequest, ct);
            await context.BotClient.SendMessage(
                context.ChatId,
                $"📈 Fetching learning statistics{(string.IsNullOrEmpty(agentId) ? " (system-wide)" : $" for {agentId}")}...",
                cancellationToken: ct);
        }
    }

    internal sealed class ModelsCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/models";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            var modelsRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = "lucifer",
                Type = CoreMessageType.Query,
                Content = "list_models",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "models"
                }
            };

            await context.MessageBus.PublishAsync(modelsRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, "🤖 Fetching available LLM models...", cancellationToken: ct);
        }
    }

    internal sealed class SuspendCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/suspend";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            if (context.Parts.Length < 2)
            {
                await context.BotClient.SendMessage(
                    context.ChatId,
                    "❌ Usage: `/suspend <agent_id>`\n" +
                    "Example: `/suspend agent_abc123`",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }

            var agentId = context.Parts[1];

            var suspendRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = agentId,
                Type = CoreMessageType.Command,
                Content = "suspend",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "suspend"
                }
            };

            await context.MessageBus.PublishAsync(suspendRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, $"😴 Suspending agent {agentId}...", cancellationToken: ct);
        }
    }

    internal sealed class ResumeCommandHandler : ITelegramCommandHandler
    {
        public string Command => "/resume";

        public async Task HandleAsync(TelegramCommandContext context, CancellationToken ct)
        {
            if (context.Parts.Length < 2)
            {
                await context.BotClient.SendMessage(
                    context.ChatId,
                    "❌ Usage: `/resume <agent_id>`\n" +
                    "Example: `/resume agent_abc123`",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }

            var agentId = context.Parts[1];

            var resumeRequest = new AgentMessage
            {
                FromAgentId = "telegram",
                ToAgentId = agentId,
                Type = CoreMessageType.Command,
                Content = "resume",
                Payload = new Dictionary<string, object>
                {
                    ["telegram_chat_id"] = context.ChatId,
                    ["command"] = "resume"
                }
            };

            await context.MessageBus.PublishAsync(resumeRequest, ct);
            await context.BotClient.SendMessage(context.ChatId, $"🔥 Resuming agent {agentId}...", cancellationToken: ct);
        }
    }
}
