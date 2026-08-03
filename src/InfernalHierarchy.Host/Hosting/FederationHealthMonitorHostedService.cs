using Microsoft.Extensions.Configuration;

namespace InfernalHierarchy.Host.Hosting;

internal sealed class FederationHealthMonitorHostedService : BackgroundService
{
    private readonly IFederationService _federationService;
    private readonly ILogger<FederationHealthMonitorHostedService> _logger;
    private readonly TimeSpan _interval;

    public FederationHealthMonitorHostedService(
        IFederationService federationService,
        IConfiguration configuration,
        ILogger<FederationHealthMonitorHostedService> logger)
    {
        _federationService = federationService;
        _logger = logger;

        var configuredSeconds = configuration.GetValue<int?>("Federation:HealthMonitorIntervalSeconds") ?? 30;
        configuredSeconds = Math.Clamp(configuredSeconds, 5, 300);
        _interval = TimeSpan.FromSeconds(configuredSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Federation health monitor started (interval={IntervalSeconds}s)", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _federationService.MonitorInstanceHealthAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Federation health monitor cycle failed");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Federation health monitor stopped");
    }
}
