using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class OllamaOptionsValidator : IValidateOptions<OllamaOptions>
{
    private readonly ILogger<OllamaOptionsValidator> _logger;

    public OllamaOptionsValidator(ILogger<OllamaOptionsValidator> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, OllamaOptions options)
    {
        var errors = new List<string>();

        if (options.BaseUrl is null)
        {
            errors.Add("Ollama:BaseUrl is required");
        }
        else if (!options.BaseUrl.IsAbsoluteUri)
        {
            errors.Add($"Ollama:BaseUrl must be an absolute URI: {options.BaseUrl}");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultModel))
        {
            errors.Add("Ollama:DefaultModel is required");
        }

        if (options.MaxTokens <= 0)
        {
            errors.Add("Ollama:MaxTokens must be greater than 0");
        }

        if (options.RequestTimeoutSeconds <= 0)
        {
            errors.Add("Ollama:RequestTimeoutSeconds must be greater than 0");
        }

        if (options.Temperature < 0 || options.Temperature > 2)
        {
            _logger.LogWarning(
                "⚠️ Ollama:Temperature {Temp} is outside recommended range 0-2",
                options.Temperature);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
