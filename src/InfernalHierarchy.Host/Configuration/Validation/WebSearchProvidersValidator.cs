using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class WebSearchProvidersValidator : IValidateOptions<SearXNGOptions>, IValidateOptions<BraveSearchOptions>
{
    private readonly IOptionsMonitor<SearXNGOptions> _searx;
    private readonly IOptionsMonitor<BraveSearchOptions> _brave;
    private readonly ILogger<WebSearchProvidersValidator> _logger;

    public WebSearchProvidersValidator(
        IOptionsMonitor<SearXNGOptions> searx,
        IOptionsMonitor<BraveSearchOptions> brave,
        ILogger<WebSearchProvidersValidator> logger)
    {
        _searx = searx;
        _brave = brave;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, SearXNGOptions options)
    {
        WarnIfAllDisabled();
        return ValidateOptionsResult.Success;
    }

    public ValidateOptionsResult Validate(string? name, BraveSearchOptions options)
    {
        WarnIfAllDisabled();
        return ValidateOptionsResult.Success;
    }

    private void WarnIfAllDisabled()
    {
        var searxEnabled = _searx.CurrentValue.Enabled;
        var braveEnabled = _brave.CurrentValue.Enabled;

        if (!searxEnabled && !braveEnabled)
        {
            _logger.LogWarning(
                "⚠️ Both SearXNG and Brave Search are disabled. Web search functionality will not work.");
        }
    }
}
