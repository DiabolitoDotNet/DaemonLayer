using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Marketplace;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Hosting;

/// <summary>
/// Discovers and hot-loads tool plugins from external assemblies at runtime.
/// </summary>
internal sealed class ToolMarketplaceHostedService : BackgroundService
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IServiceProvider _services;
    private readonly IToolPluginLoader _loader;
    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<ToolMarketplaceOptions> _optionsMonitor;
    private readonly ILogger<ToolMarketplaceHostedService> _logger;

    private readonly Dictionary<string, DateTime> _loadedWriteTimesUtc = new(StringComparer.OrdinalIgnoreCase);

    public ToolMarketplaceHostedService(
        IToolRegistry toolRegistry,
        IServiceProvider services,
        IToolPluginLoader loader,
        IHostEnvironment env,
        IOptionsMonitor<ToolMarketplaceOptions> optionsMonitor,
        ILogger<ToolMarketplaceHostedService> logger)
    {
        _toolRegistry = toolRegistry;
        _services = services;
        _loader = loader;
        _env = env;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var pluginsDir = ResolvePluginsDirectory(options.PluginsDirectory);
            if (!Directory.Exists(pluginsDir))
            {
                _logger.LogWarning("Tool marketplace enabled but directory does not exist: {Dir}", pluginsDir);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.RescanIntervalSeconds)), stoppingToken).ConfigureAwait(false);
                continue;
            }

            await ScanOnceAsync(pluginsDir, options, stoppingToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.RescanIntervalSeconds)), stoppingToken).ConfigureAwait(false);
        }
    }

    private string ResolvePluginsDirectory(string configured)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));
    }

    private async Task ScanOnceAsync(string pluginsDir, ToolMarketplaceOptions options, CancellationToken ct)
    {
        foreach (var fileName in options.AllowedPluginFiles)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var pluginPath = Path.Combine(pluginsDir, fileName);
            if (!File.Exists(pluginPath))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(pluginPath);
            }
            catch
            {
                continue;
            }

            if (info.Length > options.MaxPluginBytes)
            {
                _logger.LogWarning("Skipping plugin {Path}: file too large ({Bytes} bytes)", pluginPath, info.Length);
                continue;
            }

            var writeTime = info.LastWriteTimeUtc;
            if (_loadedWriteTimesUtc.TryGetValue(pluginPath, out var lastWrite) && lastWrite == writeTime)
            {
                continue;
            }

            var (assembly, loadResult) = await _loader.LoadAssemblyAsync(pluginPath, ct).ConfigureAwait(false);
            if (assembly == null || !loadResult.Loaded)
            {
                _loadedWriteTimesUtc[pluginPath] = writeTime;
                continue;
            }

            var tools = ToolPluginDiscovery.CreateTools(assembly, _services, _logger);
            foreach (var tool in tools)
            {
                _toolRegistry.RegisterTool(tool);
            }

            _loadedWriteTimesUtc[pluginPath] = writeTime;
            _logger.LogInformation("✅ Loaded {Count} tools from plugin {Plugin}", tools.Count, pluginPath);
        }
    }
}
