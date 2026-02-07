using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class ToolResultCacheOptionsValidator : IValidateOptions<ToolResultCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, ToolResultCacheOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("ToolCache options are null");
        }

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.DefaultTtl <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("ToolCache:DefaultTtl must be > 0 when ToolCache is enabled.");
        }

        // Keep this lightweight and realistic.
        var min = TimeSpan.FromMinutes(5);
        var max = TimeSpan.FromMinutes(30);
        if (options.DefaultTtl < min || options.DefaultTtl > max)
        {
            return ValidateOptionsResult.Fail($"ToolCache:DefaultTtl should be between {min.TotalMinutes:F0} and {max.TotalMinutes:F0} minutes.");
        }

        foreach (var (tool, ov) in options.Tools)
        {
            if (ov.Ttl is not null)
            {
                if (ov.Ttl.Value <= TimeSpan.Zero)
                {
                    return ValidateOptionsResult.Fail($"ToolCache:Tools:{tool}:Ttl must be > 0.");
                }

                if (ov.Ttl.Value < min || ov.Ttl.Value > max)
                {
                    return ValidateOptionsResult.Fail($"ToolCache:Tools:{tool}:Ttl should be between {min.TotalMinutes:F0} and {max.TotalMinutes:F0} minutes.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }
}
