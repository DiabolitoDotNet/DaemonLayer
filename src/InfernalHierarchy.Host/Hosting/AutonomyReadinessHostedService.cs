namespace InfernalHierarchy.Host.Hosting;

internal sealed class AutonomyReadinessHostedService : IHostedService
{
    private readonly ILogger<AutonomyReadinessHostedService> _logger;
    private readonly IToolRegistry _toolRegistry;
    private readonly IOptions<AutonomyReadinessOptions> _readinessOptions;
    private readonly IOptions<EmailInboxQueryOptions> _inboxOptions;
    private readonly AutonomyReadinessReportStore _store;

    public AutonomyReadinessHostedService(
        ILogger<AutonomyReadinessHostedService> logger,
        IToolRegistry toolRegistry,
        IOptions<AutonomyReadinessOptions> readinessOptions,
        IOptions<EmailInboxQueryOptions> inboxOptions,
        AutonomyReadinessReportStore store)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _readinessOptions = readinessOptions;
        _inboxOptions = inboxOptions;
        _store = store;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _readinessOptions.Value;
        if (!options.Enabled)
        {
            _store.Set(new AutonomyReadinessReport(DateTime.UtcNow, true, Array.Empty<CapabilityReadinessItem>()));
            return Task.CompletedTask;
        }

        var items = new List<CapabilityReadinessItem>(options.CriticalCapabilities.Length);

        foreach (var capability in options.CriticalCapabilities)
        {
            var normalized = capability.Trim();
            var toolRegistered = _toolRegistry.GetTool(normalized) is not null;

            if (string.Equals(normalized, "email_inbox_query", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = _inboxOptions.Value;
                var cfgReady = cfg.Enabled
                    && !string.IsNullOrWhiteSpace(cfg.Host)
                    && !string.IsNullOrWhiteSpace(cfg.Username)
                    && !string.IsNullOrWhiteSpace(cfg.Password);

                var ready = toolRegistered && cfgReady;
                var reason = ready
                    ? "ready"
                    : !toolRegistered
                        ? "tool_not_registered"
                        : "configuration_incomplete_or_disabled";

                items.Add(new CapabilityReadinessItem(normalized, ready, toolRegistered, cfgReady, reason));
                continue;
            }

            var genericReady = toolRegistered;
            items.Add(new CapabilityReadinessItem(
                normalized,
                genericReady,
                toolRegistered,
                ConfigurationReady: true,
                Reason: genericReady ? "ready" : "tool_not_registered"));
        }

        var allReady = items.All(i => i.Ready);
        var report = new AutonomyReadinessReport(DateTime.UtcNow, allReady, items);
        _store.Set(report);

        if (allReady)
        {
            _logger.LogInformation("Autonomy readiness preflight passed for {Count} critical capabilities.", items.Count);
        }
        else
        {
            var failed = string.Join(", ", items.Where(i => !i.Ready).Select(i => $"{i.Capability}:{i.Reason}"));
            _logger.LogWarning("Autonomy readiness preflight has unmet capabilities: {Failed}", failed);

            if (options.FailStartupOnCriticalNotReady)
            {
                throw new InvalidOperationException($"Autonomy readiness failed: {failed}");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
