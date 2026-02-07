using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Host.Tools;

internal sealed class ToolCacheStartupService : IHostedService
{
    private readonly IToolResultCacheStore _cacheStore;
    private readonly IOptions<ToolResultCacheOptions> _options;
    private readonly ILogger<ToolCacheStartupService> _logger;

    public ToolCacheStartupService(
        IToolResultCacheStore cacheStore,
        IOptions<ToolResultCacheOptions> options,
        ILogger<ToolCacheStartupService> logger)
    {
        _cacheStore = cacheStore;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled || !options.ClearOnStartup)
        {
            return;
        }

        _logger.LogInformation("🧹 ToolCache ClearOnStartup enabled; clearing persisted tool cache");
        await _cacheStore.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
