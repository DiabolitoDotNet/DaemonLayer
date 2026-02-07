
namespace InfernalHierarchy.Host.Configuration.Validation;

internal sealed class ToolMarketplaceOptionsValidator : IValidateOptions<ToolMarketplaceOptions>
{
    public ValidateOptionsResult Validate(string? name, ToolMarketplaceOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.PluginsDirectory))
        {
            failures.Add("ToolMarketplace:PluginsDirectory is required when ToolMarketplace:Enabled=true");
        }
        else
        {
            var root = options.PluginsDirectory;
            if (!Path.IsPathRooted(root))
            {
                if (root.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    failures.Add("ToolMarketplace:PluginsDirectory contains invalid path characters");
                }
            }
            else
            {
                if (!Directory.Exists(root))
                {
                    failures.Add("ToolMarketplace:PluginsDirectory must exist when absolute path is provided");
                }
            }
        }

        if (options.AllowedPluginFiles.Count == 0)
        {
            failures.Add("ToolMarketplace:AllowedPluginFiles must be non-empty when ToolMarketplace:Enabled=true");
        }

        if (options.MaxPluginBytes <= 0)
        {
            failures.Add("ToolMarketplace:MaxPluginBytes must be > 0");
        }

        if (options.RescanIntervalSeconds <= 0)
        {
            failures.Add("ToolMarketplace:RescanIntervalSeconds must be > 0");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
