using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Agents.Orchestration;

/// <summary>
/// Main orchestrator managing the hierarchy and Lucifer (Supreme Agent)
/// </summary>
public class AgentOrchestrator : BackgroundService
{
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IAgentFactory _agentFactory;
    private readonly IMessageBus _messageBus;
    private readonly HierarchyOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private IAgent? _mainAgent;

    public AgentOrchestrator(
        IAgentFactory agentFactory,
        IMessageBus messageBus,
        IOptions<HierarchyOptions> options,
        ILogger<AgentOrchestrator> logger,
        IServiceProvider serviceProvider)
    {
        _agentFactory = agentFactory;
        _messageBus = messageBus;
        _options = options.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
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

            // Bootstrap council/supervisor agents so delegation is possible immediately.
            await BootstrapAgentsAsync(stoppingToken);

            // Keep the orchestrator running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 Orchestrator shutdown requested");
        }
        catch (Exception ex)
        {
            // Use centralized exception handling if available
            var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();
            
            if (exceptionHandler != null)
            {
                var handlingResult = await exceptionHandler.HandleExceptionAsync(
                    ex,
                    "OrchestratorStartup");
                
                _logger.LogCritical(
                    ex,
                    "🔥 Orchestrator failed | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                    handlingResult.Category,
                    handlingResult.ShouldRetry,
                    handlingResult.CorrelationId);
            }
            else
            {
                _logger.LogError(ex, "💀 Orchestrator failed");
            }
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
                var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();
                if (exceptionHandler != null)
                {
                    var handlingResult = await exceptionHandler.HandleExceptionAsync(
                        ex,
                        $"StopAgent_{_mainAgent.Name}");
                    
                    _logger.LogError(
                        ex,
                        "Failed to stop main agent {AgentName} | Category: {Category} | CorrelationId: {CorrelationId}",
                        _mainAgent.Name,
                        handlingResult.Category,
                        handlingResult.CorrelationId);
                }
                else
                {
                    _logger.LogError(ex, "Failed to stop main agent {AgentName}", _mainAgent.Name);
                }
            }
        }

        // Stop all other agents gracefully
        var allAgents = _agentFactory
            .GetAllAgents()
            .Where(a => _mainAgent == null || a.Id != _mainAgent.Id)
            .ToList();

        foreach (var agent in allAgents)
        {
            try
            {
                await agent.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();
                if (exceptionHandler != null)
                {
                    var handlingResult = await exceptionHandler.HandleExceptionAsync(
                        ex,
                        $"StopAgent_{agent.Name}");
                    
                    _logger.LogError(
                        ex,
                        "Failed to stop agent {AgentName} | Category: {Category} | CorrelationId: {CorrelationId}",
                        agent.Name,
                        handlingResult.Category,
                        handlingResult.CorrelationId);
                }
                else
                {
                    _logger.LogError(ex, "Failed to stop agent {AgentName}", agent.Name);
                }
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

    private async Task BootstrapAgentsAsync(CancellationToken ct)
    {
        if (_options.BootstrapCouncilAgents != true)
        {
            return;
        }

        var bootstrap = _options.BootstrapAgents ?? new List<BootstrapAgentOptions>();
        if (bootstrap.Count == 0)
        {
            return;
        }

        var parentId = _mainAgent?.Id;
        var existingByName = _agentFactory
            .GetAllAgents()
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var started = 0;
        foreach (var spec in bootstrap)
        {
            ct.ThrowIfCancellationRequested();

            if (spec is null || string.IsNullOrWhiteSpace(spec.Name))
            {
                continue;
            }

            if (existingByName.ContainsKey(spec.Name))
            {
                continue;
            }

            try
            {
                _logger.LogInformation("🜂 Summoning bootstrap agent: {Name} ({Rank})...", spec.Name, spec.Rank);
                var agent = await _agentFactory.CreateAgentAsync(spec.Name, spec.Rank, parentId, ct);
                await agent.StartAsync(ct);
                started++;
            }
            catch (InvalidOperationException ex)
            {
                // Persona not found, or factory validation failure.
                _logger.LogWarning(ex, "Bootstrap agent '{Name}' could not be created", spec.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bootstrap agent '{Name}' failed to start", spec.Name);
            }
        }

        if (started > 0)
        {
            _logger.LogInformation("✅ Bootstrap complete | started={Started}", started);
        }
    }
}

public class HierarchyOptions
{
    public int MaxAgentDepth { get; set; } = 4;
    public string MainAgentName { get; set; } = "Lucifer";
    public string MainAgentPersonaPath { get; set; } = "souls/lucifer.json";

    /// <summary>
    /// When enabled, the orchestrator will summon additional council/supervisor agents on startup
    /// so the Supreme agent can delegate immediately.
    /// </summary>
    public bool BootstrapCouncilAgents { get; set; } = true;

    /// <summary>
    /// Logical name of the supervisor agent persona. Used by projection services to forward telemetry.
    /// </summary>
    public string SupervisorAgentName { get; set; } = "Orobas";

    /// <summary>
    /// Agents to summon at startup.
    /// Defaults establish a council (Baal/Asmodeus/Vassago) plus Orobas for supervision.
    /// </summary>
    public List<BootstrapAgentOptions> BootstrapAgents { get; set; } = new()
    {
        new BootstrapAgentOptions { Name = "Baal", Rank = AgentRank.Prince },
        new BootstrapAgentOptions { Name = "Asmodeus", Rank = AgentRank.Prince },
        new BootstrapAgentOptions { Name = "Vassago", Rank = AgentRank.Duke },
        new BootstrapAgentOptions { Name = "Orobas", Rank = AgentRank.Duke }
    };
}

public sealed class BootstrapAgentOptions
{
    public string Name { get; set; } = string.Empty;
    public AgentRank Rank { get; set; } = AgentRank.Worker;
}
