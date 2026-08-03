namespace InfernalHierarchy.Host.Hosting;

internal sealed class AutonomyReadinessHostedService : IHostedService
{
    private readonly ILogger<AutonomyReadinessHostedService> _logger;
    private readonly IToolRegistry _toolRegistry;
    private readonly IOptions<AutonomyReadinessOptions> _readinessOptions;
    private readonly IOptions<EmailInboxQueryOptions> _inboxOptions;
    private readonly IOptions<EmailNotificationOptions> _emailOptions;
    private readonly IOptions<TelegramOptions> _telegramOptions;
    private readonly AutonomyReadinessReportStore _store;

    public AutonomyReadinessHostedService(
        ILogger<AutonomyReadinessHostedService> logger,
        IToolRegistry toolRegistry,
        IOptions<AutonomyReadinessOptions> readinessOptions,
        IOptions<EmailInboxQueryOptions> inboxOptions,
        IOptions<EmailNotificationOptions> emailOptions,
        IOptions<TelegramOptions> telegramOptions,
        AutonomyReadinessReportStore store)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _readinessOptions = readinessOptions;
        _inboxOptions = inboxOptions;
        _emailOptions = emailOptions;
        _telegramOptions = telegramOptions;
        _store = store;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _readinessOptions.Value;
        if (!options.Enabled)
        {
            _store.Set(new AutonomyReadinessReport(DateTime.UtcNow, options.CatalogVersion, true, Array.Empty<CapabilityReadinessItem>()));
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

                items.Add(new CapabilityReadinessItem(
                    normalized,
                    ready,
                    toolRegistered,
                    cfgReady,
                    reason,
                    ConfigurationDependencies:
                    [
                        "EmailInbox:Enabled",
                        "EmailInbox:Host",
                        "EmailInbox:Username",
                        "EmailInbox:Password"
                    ]));
                continue;
            }

            if (string.Equals(normalized, "email_send", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = _emailOptions.Value;
                var cfgReady = cfg.Enabled
                    && !string.IsNullOrWhiteSpace(cfg.Host)
                    && !string.IsNullOrWhiteSpace(cfg.Username)
                    && !string.IsNullOrWhiteSpace(cfg.Password)
                    && !string.IsNullOrWhiteSpace(cfg.FromAddress);

                var ready = toolRegistered && cfgReady;
                var reason = ready
                    ? "ready"
                    : !toolRegistered
                        ? "tool_not_registered"
                        : "configuration_incomplete_or_disabled";

                items.Add(new CapabilityReadinessItem(
                    normalized,
                    ready,
                    toolRegistered,
                    cfgReady,
                    reason,
                    ConfigurationDependencies:
                    [
                        "Email:Enabled",
                        "Email:Host",
                        "Email:Username",
                        "Email:Password",
                        "Email:FromAddress"
                    ]));
                continue;
            }

            if (string.Equals(normalized, "send_telegram", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = _telegramOptions.Value;
                var cfgReady = !string.IsNullOrWhiteSpace(cfg.BotToken);

                var ready = toolRegistered && cfgReady;
                var reason = ready
                    ? "ready"
                    : !toolRegistered
                        ? "tool_not_registered"
                        : "configuration_incomplete_or_disabled";

                items.Add(new CapabilityReadinessItem(
                    normalized,
                    ready,
                    toolRegistered,
                    cfgReady,
                    reason,
                    ConfigurationDependencies:
                    ["Telegram:BotToken"]));
                continue;
            }

            var genericReady = toolRegistered;
            items.Add(new CapabilityReadinessItem(
                normalized,
                genericReady,
                toolRegistered,
                ConfigurationReady: true,
                Reason: genericReady ? "ready" : "tool_not_registered",
                ConfigurationDependencies: Array.Empty<string>()));
        }

        var allReady = items.All(i => i.Ready);
        var report = new AutonomyReadinessReport(DateTime.UtcNow, options.CatalogVersion, allReady, items);
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
