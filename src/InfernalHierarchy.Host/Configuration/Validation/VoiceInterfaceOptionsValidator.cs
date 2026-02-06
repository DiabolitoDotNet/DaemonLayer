using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class VoiceInterfaceOptionsValidator : IValidateOptions<VoiceInterfaceOptions>
{
    public ValidateOptionsResult Validate(string? name, VoiceInterfaceOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.MaxUploadBytes <= 0) failures.Add("Voice:MaxUploadBytes must be > 0");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
