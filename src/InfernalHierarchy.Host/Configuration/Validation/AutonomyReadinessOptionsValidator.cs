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

        var invalid = options.CriticalCapabilities
            .Where(c => string.IsNullOrWhiteSpace(c))
            .ToList();

        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("AutonomyReadiness:CriticalCapabilities cannot contain empty entries.");
    }
}
