namespace InfernalHierarchy.Host.Security;

internal sealed class ResourceLimitToolExecutionLimiter : IToolExecutionLimiter
{
    private readonly ResourceLimitService _resourceLimitService;
    private readonly MetricsCollector? _metrics;

    public ResourceLimitToolExecutionLimiter(ResourceLimitService resourceLimitService, MetricsCollector? metrics = null)
    {
        _resourceLimitService = resourceLimitService;
        _metrics = metrics;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        try
        {
            return await _resourceLimitService.ExecuteToolWithLimitAsync(operation, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _metrics?.IncrementCounter("tools.timeout.total");
            throw;
        }
    }
}