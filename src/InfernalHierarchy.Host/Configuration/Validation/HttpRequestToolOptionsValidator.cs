using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class HttpRequestToolOptionsValidator : IValidateOptions<HttpRequestToolOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpRequestToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.TimeoutMs <= 0) failures.Add("HttpTool:TimeoutMs must be > 0");
        if (options.MaxResponseBytes <= 0) failures.Add("HttpTool:MaxResponseBytes must be > 0");

        if (options.AllowedMethods is null || options.AllowedMethods.Count == 0)
        {
            failures.Add("HttpTool:AllowedMethods must not be empty when HttpTool:Enabled=true");
        }

        if (options.AllowedHosts is null || options.AllowedHosts.Count == 0)
        {
            failures.Add("HttpTool:AllowedHosts must not be empty when HttpTool:Enabled=true");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
