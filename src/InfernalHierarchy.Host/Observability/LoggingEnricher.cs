using Serilog.Core;
using Serilog.Events;

namespace InfernalHierarchy.Host.Observability;

/// <summary>
/// Enriches logs with structured context properties
/// </summary>
public class LoggingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Add application name
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Application", "InfernalHierarchy"));

        // Add environment (will be set from config)
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Environment", environment));

        // Add process ID
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ProcessId", Environment.ProcessId));
    }
}

/// <summary>
/// Enricher for agent-specific context
/// </summary>
public class AgentContextEnricher : ILogEventEnricher
{
    private readonly AsyncLocal<string?> _agentId = new();
    private readonly AsyncLocal<string?> _agentName = new();
    private readonly AsyncLocal<string?> _agentRank = new();

    public string? AgentId
    {
        get => _agentId.Value;
        set => _agentId.Value = value;
    }

    public string? AgentName
    {
        get => _agentName.Value;
        set => _agentName.Value = value;
    }

    public string? AgentRank
    {
        get => _agentRank.Value;
        set => _agentRank.Value = value;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!string.IsNullOrEmpty(AgentId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("AgentId", AgentId));
        }

        if (!string.IsNullOrEmpty(AgentName))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("AgentName", AgentName));
        }

        if (!string.IsNullOrEmpty(AgentRank))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("AgentRank", AgentRank));
        }
    }
}

/// <summary>
/// Enricher for message bus context
/// </summary>
public class MessageContextEnricher : ILogEventEnricher
{
    private readonly AsyncLocal<string?> _messageId = new();
    private readonly AsyncLocal<string?> _messageType = new();
    private readonly AsyncLocal<string?> _correlationId = new();

    public string? MessageId
    {
        get => _messageId.Value;
        set => _messageId.Value = value;
    }

    public string? MessageType
    {
        get => _messageType.Value;
        set => _messageType.Value = value;
    }

    public string? CorrelationId
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!string.IsNullOrEmpty(MessageId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MessageId", MessageId));
        }

        if (!string.IsNullOrEmpty(MessageType))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MessageType", MessageType));
        }

        if (!string.IsNullOrEmpty(CorrelationId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", CorrelationId));
        }
    }
}

/// <summary>
/// Enricher for tool execution context
/// </summary>
public class ToolContextEnricher : ILogEventEnricher
{
    private readonly AsyncLocal<string?> _toolName = new();
    private readonly AsyncLocal<string?> _toolExecutionId = new();

    public string? ToolName
    {
        get => _toolName.Value;
        set => _toolName.Value = value;
    }

    public string? ToolExecutionId
    {
        get => _toolExecutionId.Value;
        set => _toolExecutionId.Value = value;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!string.IsNullOrEmpty(ToolName))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ToolName", ToolName));
        }

        if (!string.IsNullOrEmpty(ToolExecutionId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ToolExecutionId", ToolExecutionId));
        }
    }
}
