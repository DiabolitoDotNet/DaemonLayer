using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Hosting;

/// <summary>
/// Helper service to register all tools in the registry on startup.
/// </summary>
public sealed class ToolRegistrationService : IHostedService
{
    private readonly IToolRegistry _registry;
    private readonly IEnumerable<ITool> _tools;
    private readonly ILogger<ToolRegistrationService> _logger;

    public ToolRegistrationService(
        IToolRegistry registry,
        IEnumerable<ITool> tools,
        ILogger<ToolRegistrationService> logger)
    {
        _registry = registry;
        _tools = tools;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 Registering tools...");

        foreach (var tool in _tools)
        {
            _registry.RegisterTool(tool);
        }

        _logger.LogInformation("✅ Registered {Count} tools", _tools.Count());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
