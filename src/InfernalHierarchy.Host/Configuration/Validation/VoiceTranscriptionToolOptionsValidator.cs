using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class VoiceTranscriptionToolOptionsValidator : IValidateOptions<VoiceTranscriptionToolOptions>
{
    public ValidateOptionsResult Validate(string? name, VoiceTranscriptionToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ExecutablePath)) failures.Add("VoiceTranscription:ExecutablePath is required when enabled");
        if (options.TimeoutMs <= 0) failures.Add("VoiceTranscription:TimeoutMs must be > 0");
        if (options.MaxOutputBytes <= 0) failures.Add("VoiceTranscription:MaxOutputBytes must be > 0");
        if (options.MaxInputBytes <= 0) failures.Add("VoiceTranscription:MaxInputBytes must be > 0");
        if (options.AllowedExtensions is null || options.AllowedExtensions.Count == 0) failures.Add("VoiceTranscription:AllowedExtensions must not be empty when enabled");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
