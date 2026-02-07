using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Memory.Vector;

/// <summary>
/// Initializes Qdrant collections on startup when vector memory is enabled.
/// </summary>
public sealed class VectorMemoryInitializationService : IHostedService
{
    private readonly IVectorMemory _vectorMemory;
    private readonly VectorMemoryOptions _options;
    private readonly ILogger<VectorMemoryInitializationService> _logger;

    public VectorMemoryInitializationService(
        IVectorMemory vectorMemory,
        IOptions<VectorMemoryOptions> options,
        ILogger<VectorMemoryInitializationService> logger)
    {
        _vectorMemory = vectorMemory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Vector memory disabled; skipping Qdrant initialization");
            return;
        }

        try
        {
            await _vectorMemory.InitializeCollectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Do not fail the entire host on external dependency issues.
            _logger.LogError(ex, "Failed to initialize Qdrant collection; vector search will be degraded");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
