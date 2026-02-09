using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Tools.Dynamic;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Host.Tools;

internal sealed class CustomToolsStartupService : IHostedService
{
    private readonly ICustomToolStore _store;
    private readonly IToolRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ICustomToolCompiler _compiler;
    private readonly ICustomToolSecurityPolicy _policy;
    private readonly IOptionsMonitor<CustomToolsOptions> _options;
    private readonly ILogger<CustomToolsStartupService> _logger;

    public CustomToolsStartupService(
        ICustomToolStore store,
        IToolRegistry registry,
        IServiceProvider services,
        ICustomToolCompiler compiler,
        ICustomToolSecurityPolicy policy,
        IOptionsMonitor<CustomToolsOptions> options,
        ILogger<CustomToolsStartupService> logger)
    {
        _store = store;
        _registry = registry;
        _services = services;
        _compiler = compiler;
        _policy = policy;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (options.Enabled != true)
        {
            _logger.LogInformation("CustomTools disabled; skipping reload");
            return;
        }

        IReadOnlyList<CustomToolDefinition> defs;
        try
        {
            defs = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load custom tools from store");
            return;
        }

        if (defs.Count == 0)
        {
            return;
        }

        var loaded = 0;
        var blocked = 0;

        foreach (var def in defs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (def is null || !def.IsValid)
            {
                continue;
            }

            if (_registry.GetTool(def.ToolName) != null)
            {
                continue;
            }

            var policyDecision = _policy.Evaluate(def.SourceCode);
            var approved = IsApproved(def, options);

            if (!policyDecision.Allowed)
            {
                blocked++;
                _logger.LogWarning("🚫 Custom tool {Tool} rejected by policy: {Reason}", def.ToolName, policyDecision.Reason);
                continue;
            }

            if (policyDecision.RequiresManualApproval && !approved && !options.AllowUnsafeWithoutManualApproval)
            {
                blocked++;
                _logger.LogWarning(
                    "⛔ Custom tool {Tool} requires manual approval; not loading (id={Id})",
                    def.ToolName,
                    def.Id);
                continue;
            }

            var compile = await _compiler.CompileAndCreateAsync(def.SourceCode, def.ToolName, _services, _logger, cancellationToken)
                .ConfigureAwait(false);

            def.LastCompiledAt = DateTimeOffset.UtcNow;
            def.LastCompileError = compile.Success ? null : compile.Error;
            await _store.UpsertAsync(def, cancellationToken).ConfigureAwait(false);

            if (!compile.Success || compile.Tool == null)
            {
                blocked++;
                _logger.LogWarning("Failed to compile custom tool {Tool}: {Error}", def.ToolName, compile.Error);
                continue;
            }

            _registry.RegisterTool(compile.Tool);
            loaded++;
        }

        _logger.LogInformation("🔁 CustomTools reload complete | loaded={Loaded} blocked={Blocked}", loaded, blocked);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsApproved(CustomToolDefinition def, CustomToolsOptions options)
    {
        return options.ApprovedToolIds.Any(id => string.Equals(id?.Trim(), def.Id, StringComparison.OrdinalIgnoreCase))
               || options.ApprovedToolNames.Any(n => string.Equals(n?.Trim(), def.ToolName, StringComparison.OrdinalIgnoreCase));
    }
}
