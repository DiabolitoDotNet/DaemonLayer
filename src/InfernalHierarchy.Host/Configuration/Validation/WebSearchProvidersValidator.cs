using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class WebSearchProvidersValidator : IValidateOptions<SearXNGOptions>, IValidateOptions<BraveSearchOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebSearchProvidersValidator> _logger;

    public WebSearchProvidersValidator(
        IConfiguration configuration,
        ILogger<WebSearchProvidersValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, SearXNGOptions options)
    {
        WarnIfAllDisabled(searxEnabled: options.Enabled, braveEnabled: _configuration.GetValue<bool>("BraveSearch:Enabled"));
        return ValidateOptionsResult.Success;
    }

    public ValidateOptionsResult Validate(string? name, BraveSearchOptions options)
    {
        WarnIfAllDisabled(searxEnabled: _configuration.GetValue<bool>("SearXNG:Enabled"), braveEnabled: options.Enabled);
        return ValidateOptionsResult.Success;
    }

    private void WarnIfAllDisabled(bool searxEnabled, bool braveEnabled)
    {
        if (!searxEnabled && !braveEnabled)
        {
            _logger.LogWarning(
                "⚠️ Both SearXNG and Brave Search are disabled. Web search functionality will not work.");
        }
    }
}
