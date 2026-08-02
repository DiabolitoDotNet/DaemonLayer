namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class VisionToolOptionsValidator : IValidateOptions<VisionToolOptions>
{
    public ValidateOptionsResult Validate(string? name, VisionToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.TimeoutMs <= 0) failures.Add("Vision:TimeoutMs must be > 0");
        if (options.MaxOutputChars <= 0) failures.Add("Vision:MaxOutputChars must be > 0");
        if (options.MaxInputBytes <= 0) failures.Add("Vision:MaxInputBytes must be > 0");
        if (options.AllowedExtensions is null || options.AllowedExtensions.Count == 0) failures.Add("Vision:AllowedExtensions must not be empty when enabled");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}