using FluentAssertions;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Voice;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class TextToSpeechLanguageRoutingTests
{
    [Fact]
    public void Resolve_WhenLanguageIsFrench_UsesFrenchVoiceOverrides()
    {
        var options = new TextToSpeechToolOptions
        {
            PiperVoicePath = "voices/default",
            PiperSpeakerId = 0,
            FrenchPiperVoicePath = "voices/fr",
            FrenchPiperSpeakerId = 2,
            EnableLanguageVoiceSelection = true
        };

        var result = TextToSpeechLanguageRouting.Resolve(
            options,
            new Dictionary<string, object> { ["language"] = "fr-CA" },
            "Bonjour tout le monde");

        result.IsFrench.Should().BeTrue();
        result.AutoDetectedLanguage.Should().BeFalse();
        result.PiperVoicePath.Should().Be("voices/fr");
        result.SpeakerId.Should().Be(2);
        result.LanguageTag.Should().Be("fr-ca");
    }

    [Fact]
    public void Resolve_WhenFrenchDetectedFromText_UsesFrenchVoiceOverrides()
    {
        var options = new TextToSpeechToolOptions
        {
            PiperVoicePath = "voices/default",
            PiperSpeakerId = 1,
            FrenchPiperVoicePath = "voices/fr",
            FrenchPiperSpeakerId = 4,
            EnableLanguageVoiceSelection = true
        };

        var result = TextToSpeechLanguageRouting.Resolve(
            options,
            new Dictionary<string, object>(),
            "Merci beaucoup, c'est très clair.");

        result.IsFrench.Should().BeTrue();
        result.AutoDetectedLanguage.Should().BeTrue();
        result.PiperVoicePath.Should().Be("voices/fr");
        result.SpeakerId.Should().Be(4);
        result.LanguageTag.Should().Be("fr");
    }

    [Fact]
    public void Resolve_WhenNotFrench_UsesDefaultVoice()
    {
        var options = new TextToSpeechToolOptions
        {
            PiperVoicePath = "voices/default",
            PiperSpeakerId = 7,
            FrenchPiperVoicePath = "voices/fr",
            FrenchPiperSpeakerId = 9,
            EnableLanguageVoiceSelection = true
        };

        var result = TextToSpeechLanguageRouting.Resolve(
            options,
            new Dictionary<string, object> { ["language"] = "en-US" },
            "Hello world");

        result.IsFrench.Should().BeFalse();
        result.PiperVoicePath.Should().Be("voices/default");
        result.SpeakerId.Should().Be(7);
        result.LanguageTag.Should().Be("en-us");
    }

    [Fact]
    public void Resolve_WhenFrenchVoiceMissing_FallsBackToDefault()
    {
        var options = new TextToSpeechToolOptions
        {
            PiperVoicePath = "voices/default",
            PiperSpeakerId = 3,
            FrenchPiperVoicePath = string.Empty,
            FrenchPiperSpeakerId = 11,
            EnableLanguageVoiceSelection = true
        };

        var result = TextToSpeechLanguageRouting.Resolve(
            options,
            new Dictionary<string, object> { ["language"] = "fr" },
            "Bonjour");

        result.IsFrench.Should().BeTrue();
        result.PiperVoicePath.Should().Be("voices/default");
        result.SpeakerId.Should().Be(11);
    }

    [Fact]
    public void Resolve_WhenLanguageSelectionDisabled_AlwaysUsesDefaultVoice()
    {
        var options = new TextToSpeechToolOptions
        {
            PiperVoicePath = "voices/default",
            PiperSpeakerId = 5,
            FrenchPiperVoicePath = "voices/fr",
            FrenchPiperSpeakerId = 12,
            EnableLanguageVoiceSelection = false
        };

        var result = TextToSpeechLanguageRouting.Resolve(
            options,
            new Dictionary<string, object> { ["language"] = "fr" },
            "Bonjour");

        result.IsFrench.Should().BeTrue();
        result.PiperVoicePath.Should().Be("voices/default");
        result.SpeakerId.Should().Be(5);
        result.AutoDetectedLanguage.Should().BeFalse();
    }
}
