using FluentAssertions;
using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Tools.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class VoiceAndVisionOptionsValidatorTests
{
    [Fact]
    public void VoiceTranscriptionValidator_WhenSidecarEnabledAndMissingBaseUrl_Fails()
    {
        var validator = new VoiceTranscriptionToolOptionsValidator();
        var options = new VoiceTranscriptionToolOptions
        {
            Enabled = true,
            UseSidecar = true,
            SidecarTimeoutMs = 1000,
            SidecarTranscribePath = "/transcribe"
        };
        options.SidecarBaseUrl = null!;

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void TextToSpeechValidator_WhenSidecarEnabledAndMissingPath_Fails()
    {
        var validator = new TextToSpeechToolOptionsValidator();
        var options = new TextToSpeechToolOptions
        {
            Enabled = true,
            UseSidecar = true,
            SidecarBaseUrl = new Uri("http://localhost:5091"),
            SidecarSpeakPath = string.Empty,
            SidecarTimeoutMs = 1000,
            OutputExtension = ".wav"
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void VisionValidator_WhenEnabledAndExtensionsMissing_Fails()
    {
        var validator = new VisionToolOptionsValidator();
        var options = new VisionToolOptions
        {
            Enabled = true,
            TimeoutMs = 1000,
            MaxInputBytes = 1024,
            MaxOutputChars = 100
        };
        options.AllowedExtensions.Clear();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void VisionValidator_WhenEnabledAndValid_Succeeds()
    {
        var validator = new VisionToolOptionsValidator();
        var options = new VisionToolOptions
        {
            Enabled = true,
            TimeoutMs = 1000,
            MaxInputBytes = 1024,
            MaxOutputChars = 100
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
