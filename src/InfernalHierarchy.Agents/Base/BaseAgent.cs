
namespace InfernalHierarchy.Agents.Base;

/// <summary>
/// Base abstract agent implementing common functionality
/// </summary>
public abstract class BaseAgent : IAgent
{
    protected readonly ILogger<BaseAgent> _logger;
    protected readonly IMessageBus _messageBus;
    protected readonly ISharedMemory _sharedMemory;
    protected readonly IToolRegistry _toolRegistry;
    protected CancellationTokenSource? _cts;
    protected Task? _executionTask;

    public string Id { get; }
    public string Name { get; }
    public AgentRank Rank { get; }
    public AgentStatus Status { get; protected set; }
    public Persona Persona { get; }
    public string? ParentAgentId { get; }

    protected BaseAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        ILogger<BaseAgent> logger)
    {
        Id = agent.Id;
        Name = agent.Name;
        Rank = agent.Rank;
        ParentAgentId = agent.ParentAgentId;
        Persona = persona;
        Status = AgentStatus.Idle;

        _messageBus = messageBus;
        _sharedMemory = sharedMemory;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public virtual async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("🔥 {AgentName} ({Rank}) awakening...", Name, Rank);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _executionTask = RunExecutionLoopAsync(_cts.Token);

        Status = AgentStatus.Idle;
        await Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("💀 {AgentName} shutting down...", Name);

        Status = AgentStatus.Terminated;
        _cts?.Cancel();

        if (_executionTask != null)
        {
            await _executionTask;
        }
    }

    public virtual async Task SuspendAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("😴 {AgentName} suspending (hibernation)...", Name);

        if (Status == AgentStatus.Suspended)
        {
            _logger.LogWarning("{AgentName} is already suspended", Name);
            return;
        }

        if (Status == AgentStatus.Terminated)
        {
            _logger.LogWarning("Cannot suspend terminated agent {AgentName}", Name);
            return;
        }

        Status = AgentStatus.Suspended;
        _cts?.Cancel(); // Stop execution loop

        if (_executionTask != null)
        {
            await _executionTask;
        }

        _logger.LogInformation("✅ {AgentName} suspended successfully", Name);
    }

    public virtual async Task ResumeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("🔥 {AgentName} resuming from suspension...", Name);

        if (Status != AgentStatus.Suspended)
        {
            _logger.LogWarning("{AgentName} is not suspended (Status: {Status})", Name, Status);
            return;
        }

        // Restart execution loop
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _executionTask = RunExecutionLoopAsync(_cts.Token);

        Status = AgentStatus.Idle;
        _logger.LogInformation("✅ {AgentName} resumed successfully", Name);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Main execution loop - listens to message bus and processes tasks
    /// </summary>
    protected virtual async Task RunExecutionLoopAsync(CancellationToken ct)
    {
        var consecutiveErrors = 0;
        const int maxConsecutiveErrors = 5;
        const int errorBackoffMs = 1000;

        try
        {
            await foreach (var message in _messageBus.SubscribeAsync(Id, ct))
            {
                try
                {
                    if (message.Type == MessageType.Task ||
                        message.Type == MessageType.Query ||
                        message.Type == MessageType.Command ||
                        message.Type == MessageType.CollaborationRequest)
                    {
                        _logger.LogInformation("📨 {AgentName} received {MessageType}: {Content}",
                            Name, message.Type, message.Content);

                        var response = await ProcessTaskAsync(message, ct);
                        await _messageBus.PublishAsync(response, ct);

                        // Reset error counter on success
                        consecutiveErrors = 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogError(ex,
                        "❌ {AgentName} failed to process message {MessageId} (Error {Count}/{Max})",
                        Name, message.Id, consecutiveErrors, maxConsecutiveErrors);

                    // Send error response back to sender
                    var errorResponse = new AgentMessage
                    {
                        FromAgentId = Id,
                        ToAgentId = message.FromAgentId,
                        Type = MessageType.Report,
                        Content = $"❌ Error processing task: {ex.Message}"
                    };

                    try
                    {
                        await _messageBus.PublishAsync(errorResponse, ct);
                    }
                    catch (Exception publishEx)
                    {
                        _logger.LogError(publishEx, "Failed to publish error response");
                    }

                    // If too many consecutive errors, implement backoff
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        _logger.LogCritical(
                            "💀 {AgentName} encountered {Count} consecutive errors. Applying backoff...",
                            Name, consecutiveErrors);

                        await Task.Delay(errorBackoffMs * consecutiveErrors, ct);
                        consecutiveErrors = 0; // Reset after backoff
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{AgentName} execution loop cancelled", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💀 {AgentName} execution loop failed critically", Name);
            Status = AgentStatus.Terminated;
        }
    }

    public abstract Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default);

    public virtual bool CanCreateSubAgent(AgentRank targetRank)
    {
        // Hierarchical rules: Supreme > Prince > Duke > Worker
        return Rank switch
        {
            AgentRank.Supreme => true, // Can create anyone
            AgentRank.Prince => targetRank >= AgentRank.Duke,
            AgentRank.Duke => targetRank == AgentRank.Worker,
            AgentRank.Worker => false,
            _ => false
        };
    }

    /// <summary>
    /// Build context for LLM prompt including system prompt, memory, and current task
    /// </summary>
    protected virtual async Task<string> BuildContextAsync(AgentMessage task, CancellationToken ct)
    {
        var context = $"""
            # System Prompt
            {Persona.SystemPrompt}

            # Agent Identity
            Name: {Name}
            Rank: {Rank}
            Title: {Persona.DemonTitle}
            Specializations: {string.Join(", ", Persona.Specializations)}

            # Recent Memory
            """;

        // Add recent decisions
        var decisions = await _sharedMemory.GetRecentDecisionsAsync(5, ct);
        if (decisions.Any())
        {
            context += "\n\n## Recent Decisions:\n";
            foreach (var d in decisions)
            {
                context += $"- {d.Action} (by {d.CreatedBy}): {d.Reasoning}\n";
            }
        }

        // Add task
        context += $"""

            # Current Task
            From: {task.FromAgentId}
            Content: {task.Content}

            # Available Tools
            {string.Join(", ", Persona.AvailableTools)}

            Now, using the ReAct pattern (Thought → Action → Observation), process this task.
            """;

        return context;
    }
}
