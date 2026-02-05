using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class ToolRateLimitingOptionsValidator : IValidateOptions<ToolRateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, ToolRateLimitingOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        ValidateRule("ToolRateLimiting:DefaultRule", options.DefaultRule, failures);

        foreach (var kvp in options.RankDefaults)
        {
            ValidateRule($"ToolRateLimiting:RankDefaults:{kvp.Key}", kvp.Value, failures);
        }

        foreach (var tool in options.Tools)
        {
            var toolName = tool.Key;
            var ov = tool.Value;

            if (ov.DefaultRule != null)
            {
                ValidateRule($"ToolRateLimiting:Tools:{toolName}:DefaultRule", ov.DefaultRule, failures);
            }

            if (ov.RankDefaults != null)
            {
                foreach (var rank in ov.RankDefaults)
                {
                    ValidateRule($"ToolRateLimiting:Tools:{toolName}:RankDefaults:{rank.Key}", rank.Value, failures);
                }
            }
        }

        if (options.IdleEntryExpirationSeconds < 30)
        {
            failures.Add("ToolRateLimiting:IdleEntryExpirationSeconds must be >= 30");
        }

        if (options.PruneEveryNChecks < 0)
        {
            failures.Add("ToolRateLimiting:PruneEveryNChecks must be >= 0");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRule(string prefix, FixedWindowRateLimitRule rule, List<string> failures)
    {
        if (rule.PermitLimit <= 0)
        {
            failures.Add($"{prefix}:PermitLimit must be > 0");
        }

        if (rule.WindowSeconds <= 0)
        {
            failures.Add($"{prefix}:WindowSeconds must be > 0");
        }

        if (rule.WindowSeconds > 24 * 60 * 60)
        {
            failures.Add($"{prefix}:WindowSeconds must be <= 86400");
        }
    }
}
