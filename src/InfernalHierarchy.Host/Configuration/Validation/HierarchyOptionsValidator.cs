using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class HierarchyOptionsValidator : IValidateOptions<HierarchyOptions>
{
    private readonly ILogger<HierarchyOptionsValidator> _logger;

    public HierarchyOptionsValidator(ILogger<HierarchyOptionsValidator> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, HierarchyOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.MainAgentName))
        {
            errors.Add("Hierarchy:MainAgentName is required");
        }

        if (string.IsNullOrWhiteSpace(options.MainAgentPersonaPath))
        {
            errors.Add("Hierarchy:MainAgentPersonaPath is required");
        }
        else
        {
            var personaPath = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                options.MainAgentPersonaPath);
            personaPath = Path.GetFullPath(personaPath);

            if (!File.Exists(personaPath))
            {
                errors.Add($"Main agent persona file not found: {personaPath}");
            }
        }

        if (options.MaxAgentDepth <= 0)
        {
            errors.Add("Hierarchy:MaxAgentDepth must be greater than 0");
        }
        else if (options.MaxAgentDepth > 10)
        {
            _logger.LogWarning(
                "⚠️ Hierarchy:MaxAgentDepth {Depth} is very high. This may cause performance issues.",
                options.MaxAgentDepth);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
