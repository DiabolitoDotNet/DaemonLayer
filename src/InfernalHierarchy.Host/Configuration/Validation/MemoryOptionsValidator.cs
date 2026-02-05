using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class MemoryOptionsValidator : IValidateOptions<MemoryOptions>
{
    public ValidateOptionsResult Validate(string? name, MemoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return ValidateOptionsResult.Fail("Memory:DatabasePath is required");
        }

        return ValidateOptionsResult.Success;
    }
}
