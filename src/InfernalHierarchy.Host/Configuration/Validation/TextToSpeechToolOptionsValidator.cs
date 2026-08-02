
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class TextToSpeechToolOptionsValidator : IValidateOptions<TextToSpeechToolOptions>
{
    public ValidateOptionsResult Validate(string? name, TextToSpeechToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.UseSidecar)
        {
            if (options.SidecarBaseUrl is null || !options.SidecarBaseUrl.IsAbsoluteUri) failures.Add("TextToSpeech:SidecarBaseUrl must be an absolute URI when UseSidecar=true");
            if (options.SidecarTimeoutMs <= 0) failures.Add("TextToSpeech:SidecarTimeoutMs must be > 0 when UseSidecar=true");
            if (string.IsNullOrWhiteSpace(options.SidecarSpeakPath)) failures.Add("TextToSpeech:SidecarSpeakPath is required when UseSidecar=true");
        }
        else if (options.UsePiperNet)
        {
            if (string.IsNullOrWhiteSpace(options.PiperVoicePath)) failures.Add("TextToSpeech:PiperVoicePath is required when TextToSpeech:UsePiperNet=true");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.ExecutablePath)) failures.Add("TextToSpeech:ExecutablePath is required when enabled and UsePiperNet=false");
            if (options.Arguments is null || options.Arguments.Count == 0) failures.Add("TextToSpeech:Arguments must not be empty when enabled and UsePiperNet=false");
        }
        if (options.TimeoutMs <= 0) failures.Add("TextToSpeech:TimeoutMs must be > 0");
        if (options.MaxOutputBytes <= 0) failures.Add("TextToSpeech:MaxOutputBytes must be > 0");
        if (options.MaxTextChars <= 0) failures.Add("TextToSpeech:MaxTextChars must be > 0");
        if (string.IsNullOrWhiteSpace(options.OutputExtension)) failures.Add("TextToSpeech:OutputExtension must be set when enabled");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
