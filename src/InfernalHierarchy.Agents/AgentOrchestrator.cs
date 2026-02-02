using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Agents;

/// <summary>
/// Main orchestrator managing the hierarchy and Lucifer (Supreme Agent)
/// </summary>
public class AgentOrchestrator : BackgroundService
{
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IAgentFactory _agentFactory;
    private readonly IMessageBus _messageBus;
    private readonly HierarchyOptions _options;
    private IAgent? _mainAgent;

    public AgentOrchestrator(
        IAgentFactory agentFactory,
        IMessageBus messageBus,
        IOptions<HierarchyOptions> options,
        ILogger<AgentOrchestrator> logger)
    {
        _agentFactory = agentFactory;
        _messageBus = messageBus;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔥 InfernalHierarchy Orchestrator starting...");

        try
        {
            // Create and start the main agent (Lucifer)
            _logger.LogInformation("👑 Summoning {MainAgent} (Supreme Agent)...", _options.MainAgentName);

            _mainAgent = await _agentFactory.CreateAgentAsync(
                _options.MainAgentName,
                AgentRank.Supreme,
                parentId: null,
                ct: stoppingToken);

            // Start the main agent
            await _mainAgent.StartAsync(stoppingToken);

            _logger.LogInformation("✅ {MainAgent} is now active and listening", _options.MainAgentName);

            // Keep the orchestrator running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 Orchestrator shutdown requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💀 Orchestrator failed");
        }
        finally
        {
            if (_mainAgent != null)
            {
                await _mainAgent.StopAsync(CancellationToken.None);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔥 Shutting down InfernalHierarchy...");

        // Stop main agent first
        if (_mainAgent != null)
        {
            try
            {
                await _mainAgent.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop main agent {AgentName}", _mainAgent.Name);
            }
        }

        // Stop all other agents gracefully
        var registry = _agentFactory as AgentFactory;
        var allAgents = registry?.GetAllAgents().ToList() ?? new List<IAgent>();

        foreach (var agent in allAgents)
        {
            try
            {
                await agent.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop agent {AgentName}", agent.Name);
            }
        }

        // Cleanup message bus if it implements cleanup
        if (_messageBus is ChannelMessageBus messageBus)
        {
            foreach (var agent in allAgents)
            {
                messageBus.CleanupAgent(agent.Id);
            }
        }

        await base.StopAsync(cancellationToken);
    }
}

public class HierarchyOptions
{
    public int MaxAgentDepth { get; set; } = 4;
    public string MainAgentName { get; set; } = "Lucifer";
    public string MainAgentPersonaPath { get; set; } = "souls/lucifer.json";
}
