using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace InfernalHierarchy.Host;

/// <summary>
/// Service that handles dynamic configuration reload for non-sensitive settings
/// </summary>
public class ConfigurationReloadService : BackgroundService
{
    private readonly ILogger<ConfigurationReloadService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<HierarchyOptions> _hierarchyOptions;
    private readonly IOptionsMonitor<MemoryOptions> _memoryOptions;
    private readonly IOptionsMonitor<SearXNGOptions> _searxngOptions;
    private readonly List<IDisposable> _changeTokenRegistrations = new();
    private int _reloadCount = 0;

    public ConfigurationReloadService(
        ILogger<ConfigurationReloadService> logger,
        IConfiguration configuration,
        IOptionsMonitor<HierarchyOptions> hierarchyOptions,
        IOptionsMonitor<MemoryOptions> memoryOptions,
        IOptionsMonitor<SearXNGOptions> searxngOptions)
    {
        _logger = logger;
        _configuration = configuration;
        _hierarchyOptions = hierarchyOptions;
        _memoryOptions = memoryOptions;
        _searxngOptions = searxngOptions;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 Configuration reload service started");

        // Register change callbacks for all monitored options
        _changeTokenRegistrations.Add(_hierarchyOptions.OnChange(OnHierarchyOptionsChanged));
        _changeTokenRegistrations.Add(_memoryOptions.OnChange(OnMemoryOptionsChanged));
        _changeTokenRegistrations.Add(_searxngOptions.OnChange(OnSearxngOptionsChanged));

        // Monitor configuration root for file changes
        ChangeToken.OnChange(
            () => _configuration.GetReloadToken(),
            () => OnConfigurationReloaded());

        _logger.LogInformation("✅ Monitoring configuration for changes");

        return Task.CompletedTask;
    }

    private void OnConfigurationReloaded()
    {
        _reloadCount++;
        _logger.LogInformation("🔄 Configuration file reloaded (reload #{Count})", _reloadCount);

        // Log current configuration state
        LogConfigurationSummary();
    }

    private void OnHierarchyOptionsChanged(HierarchyOptions newOptions, string? name)
    {
        _logger.LogInformation("🔄 Hierarchy configuration changed:");
        _logger.LogInformation("  - MainAgentName: {Name}", newOptions.MainAgentName);
        _logger.LogInformation("  - MaxAgentDepth: {Max}", newOptions.MaxAgentDepth);
    }

    private void OnMemoryOptionsChanged(MemoryOptions newOptions, string? name)
    {
        _logger.LogInformation("🔄 Memory configuration changed:");
        _logger.LogInformation("  - DatabasePath: {Path}", newOptions.DatabasePath);
    }

    private void OnSearxngOptionsChanged(SearXNGOptions newOptions, string? name)
    {
        _logger.LogInformation("🔄 SearXNG configuration changed:");
        _logger.LogInformation("  - BaseUrl: {Url}", newOptions.BaseUrl);
        _logger.LogInformation("  - Enabled: {Enabled}", newOptions.Enabled);
    }

    private void LogConfigurationSummary()
    {
        try
        {
            var hierarchyOpts = _hierarchyOptions.CurrentValue;
            var memoryOpts = _memoryOptions.CurrentValue;
            var searxngOpts = _searxngOptions.CurrentValue;

            _logger.LogInformation("📋 Current Configuration Summary:");
            _logger.LogInformation("  Hierarchy:");
            _logger.LogInformation("    - MainAgent: {Agent}", hierarchyOpts.MainAgentName);
            _logger.LogInformation("    - Max Depth: {Max}", hierarchyOpts.MaxAgentDepth);
            _logger.LogInformation("  Memory:");
            _logger.LogInformation("    - Database: {Path}", memoryOpts.DatabasePath);
            _logger.LogInformation("  Search:");
            _logger.LogInformation("    - SearXNG: {Url}", searxngOpts.BaseUrl);
            _logger.LogInformation("    - Enabled: {Enabled}", searxngOpts.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging configuration summary");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 Configuration reload service stopping...");

        // Dispose change token registrations
        foreach (var registration in _changeTokenRegistrations)
        {
            registration.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Get current reload count (for monitoring)
    /// </summary>
    public int ReloadCount => _reloadCount;
}

/// <summary>
/// Extension methods for configuration reload support
/// </summary>
public static class ConfigurationReloadExtensions
{
    /// <summary>
    /// Force reload configuration from file system
    /// </summary>
    public static void ForceReload(this IConfiguration configuration)
    {
        if (configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }
    }

    /// <summary>
    /// Check if a configuration section has changed
    /// </summary>
    public static bool HasChanged(this IConfiguration configuration, string sectionPath, Dictionary<string, string?> previousValues)
    {
        var section = configuration.GetSection(sectionPath);
        if (!section.Exists()) return false;

        foreach (var child in section.GetChildren())
        {
            var key = $"{sectionPath}:{child.Key}";
            var currentValue = child.Value;

            if (!previousValues.TryGetValue(key, out var previousValue) || previousValue != currentValue)
            {
                return true;
            }
        }

        return false;
    }
}
