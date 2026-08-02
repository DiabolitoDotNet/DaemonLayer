
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

        if (options.UseSidecar)
        {
            if (options.SidecarBaseUrl is null || !options.SidecarBaseUrl.IsAbsoluteUri) failures.Add("VoiceTranscription:SidecarBaseUrl must be an absolute URI when UseSidecar=true");
            if (options.SidecarTimeoutMs <= 0) failures.Add("VoiceTranscription:SidecarTimeoutMs must be > 0 when UseSidecar=true");
            if (string.IsNullOrWhiteSpace(options.SidecarTranscribePath)) failures.Add("VoiceTranscription:SidecarTranscribePath is required when UseSidecar=true");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.ExecutablePath)) failures.Add("VoiceTranscription:ExecutablePath is required when enabled and UseSidecar=false");
        }

        if (options.TimeoutMs <= 0) failures.Add("VoiceTranscription:TimeoutMs must be > 0");
        if (options.MaxOutputBytes <= 0) failures.Add("VoiceTranscription:MaxOutputBytes must be > 0");
        if (options.MaxInputBytes <= 0) failures.Add("VoiceTranscription:MaxInputBytes must be > 0");
        if (options.AllowedExtensions is null || options.AllowedExtensions.Count == 0) failures.Add("VoiceTranscription:AllowedExtensions must not be empty when enabled");

        if (!string.IsNullOrWhiteSpace(options.DecoderExecutablePath)
            && (options.DecoderArguments is null || options.DecoderArguments.Count == 0))
        {
            // Not strictly required because the tool provides a default ffmpeg-like argument list,
            // but if users set a decoder they typically intend to customize args.
            // Keep this lenient to avoid breaking existing configs.
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
