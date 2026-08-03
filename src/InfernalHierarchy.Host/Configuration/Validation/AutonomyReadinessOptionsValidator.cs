namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class AutonomyReadinessOptionsValidator : IValidateOptions<AutonomyReadinessOptions>
{
    public ValidateOptionsResult Validate(string? name, AutonomyReadinessOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.CriticalCapabilities is null || options.CriticalCapabilities.Length == 0)
        {
            return ValidateOptionsResult.Fail("AutonomyReadiness:CriticalCapabilities must include at least one capability when enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.CatalogVersion))
        {
            return ValidateOptionsResult.Fail("AutonomyReadiness:CatalogVersion is required when readiness checks are enabled.");
        }

        var invalid = options.CriticalCapabilities
            .Where(c => string.IsNullOrWhiteSpace(c))
            .ToList();

        var duplicates = options.CriticalCapabilities
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            return ValidateOptionsResult.Fail($"AutonomyReadiness:CriticalCapabilities contains duplicates: {string.Join(", ", duplicates)}");
        }

        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("AutonomyReadiness:CriticalCapabilities cannot contain empty entries.");
    }
}
