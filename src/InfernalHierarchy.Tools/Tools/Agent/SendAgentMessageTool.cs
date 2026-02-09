using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Agent;

/// <summary>
/// Publishes a message to another agent via the internal message bus.
/// This enables real task delegation after creating a sub-agent.
/// </summary>
public sealed class SendAgentMessageTool : ITool
{
    private readonly ILogger<SendAgentMessageTool> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IAgentRegistry _agentRegistry;

    public SendAgentMessageTool(
        ILogger<SendAgentMessageTool> logger,
        IMessageBus messageBus,
        IAgentRegistry agentRegistry)
    {
        _logger = logger;
        _messageBus = messageBus;
        _agentRegistry = agentRegistry;
    }

    public string Name => "send_agent_message";

    public string Description =>
        "Send a message (typically a Task) to another agent via the internal message bus. " +
        "Parameters: to_agent_id (required), content (required; aliases: task,message,text), " +
        "type (optional: task/report/query/command/notification, default: task), from_agent_id (optional; alias: agent_id).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!TryGetStringAny(parameters, out var toAgentId, "to_agent_id", "toAgentId", "agent_id", "target_agent_id", "targetAgentId"))
        {
            return new ToolResult { Success = false, Error = "Missing required parameter: to_agent_id" };
        }

        if (!TryGetStringAny(parameters, out var content, "content", "task", "message", "text"))
        {
            return new ToolResult { Success = false, Error = "Missing required parameter: content" };
        }

        var fromAgentId = TryGetStringAny(parameters, out var parsedFrom, "from_agent_id", "fromAgentId", "sender_agent_id", "senderAgentId", "agent_id")
            ? parsedFrom
            : "system";

        var type = MessageType.Task;
        if (TryGetStringAny(parameters, out var typeStr, "type", "message_type", "messageType"))
        {
            type = typeStr.Trim().ToLowerInvariant() switch
            {
                "report" => MessageType.Report,
                "query" => MessageType.Query,
                "command" => MessageType.Command,
                "notification" => MessageType.Notification,
                "toolresult" => MessageType.ToolResult,
                "broadcast" => MessageType.Broadcast,
                _ => MessageType.Task
            };
        }

        var targetAgent = _agentRegistry.GetAgent(toAgentId);
        if (targetAgent == null)
        {
            return new ToolResult { Success = false, Error = $"Unknown agent_id: {toAgentId}" };
        }

        var message = new AgentMessage
        {
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Type = type,
            Content = content,
            Payload = new Dictionary<string, object>()
        };

        _logger.LogInformation("📨 Sending {Type} from {From} to {To}", type, fromAgentId, toAgentId);
        await _messageBus.PublishAsync(message, ct);

        return new ToolResult
        {
            Success = true,
            Output = JsonSerializer.Serialize(new
            {
                ok = true,
                to_agent_id = toAgentId,
                to_agent_name = targetAgent.Name,
                type = type.ToString(),
                message_id = message.Id
            })
        };
    }

    private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
    {
        value = string.Empty;

        if (!parameters.TryGetValue(key, out var obj) || obj is null)
        {
            return false;
        }

        if (obj is string s)
        {
            value = s;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (obj is JsonElement el)
        {
            value = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? string.Empty,
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => el.GetRawText()
            };

            return !string.IsNullOrWhiteSpace(value);
        }

        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStringAny(Dictionary<string, object> parameters, out string value, params string[] keys)
    {
        value = string.Empty;

        foreach (var key in keys)
        {
            if (TryGetString(parameters, key, out var parsed) && !string.IsNullOrWhiteSpace(parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }
}
