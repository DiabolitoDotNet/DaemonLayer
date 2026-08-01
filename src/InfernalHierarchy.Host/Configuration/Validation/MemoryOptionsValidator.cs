
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class MemoryOptionsValidator : IValidateOptions<MemoryOptions>
{
    public ValidateOptionsResult Validate(string? name, MemoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return ValidateOptionsResult.Fail("Memory:DatabasePath is required");
        }

        var directory = Path.GetDirectoryName(options.DatabasePath);
        if (directory != null && directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return ValidateOptionsResult.Fail("Memory:DatabasePath contains invalid path characters");
        }

        return ValidateOptionsResult.Success;
    }
}
