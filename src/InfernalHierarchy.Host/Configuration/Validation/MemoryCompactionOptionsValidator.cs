namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class MemoryCompactionOptionsValidator : IValidateOptions<MemoryCompactionOptions>
{
    public ValidateOptionsResult Validate(string? name, MemoryCompactionOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (options.IntervalHours <= 0)
        {
            errors.Add("MemoryCompactionOptions:IntervalHours must be > 0 when compaction is enabled");
        }

        if (options.MinDatabaseSizeBytes <= 0)
        {
            errors.Add("MemoryCompactionOptions:MinDatabaseSizeBytes must be > 0 when compaction is enabled");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}