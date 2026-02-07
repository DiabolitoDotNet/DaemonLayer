
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class SearXngOptionsValidator : IValidateOptions<SearXNGOptions>
{
    public ValidateOptionsResult Validate(string? name, SearXNGOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.BaseUrl is null)
        {
            return ValidateOptionsResult.Fail("SearXNG:BaseUrl is required when SearXNG is enabled");
        }

        if (!options.BaseUrl.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail($"SearXNG:BaseUrl must be an absolute URI: {options.BaseUrl}");
        }

        return ValidateOptionsResult.Success;
    }
}
