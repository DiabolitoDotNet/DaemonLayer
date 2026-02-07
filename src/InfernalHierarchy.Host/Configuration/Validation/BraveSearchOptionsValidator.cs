using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class BraveSearchOptionsValidator : IValidateOptions<BraveSearchOptions>
{
    private readonly ILogger<BraveSearchOptionsValidator> _logger;

    public BraveSearchOptionsValidator(ILogger<BraveSearchOptionsValidator> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, BraveSearchOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _logger.LogWarning("⚠️ BraveSearch:ApiKey is not configured. Brave Search fallback will not work.");
        }

        return ValidateOptionsResult.Success;
    }
}
