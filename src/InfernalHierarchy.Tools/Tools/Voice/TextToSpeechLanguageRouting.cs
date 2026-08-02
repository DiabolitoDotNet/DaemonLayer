namespace InfernalHierarchy.Tools.Tools.Voice;

public sealed record TextToSpeechVoiceSelection(
    string LanguageTag,
    string PiperVoicePath,
    int SpeakerId,
    bool AutoDetectedLanguage,
    bool IsFrench);

public static class TextToSpeechLanguageRouting
{
    private static readonly string[] FrenchHints =
    [
        "bonjour",
        "merci",
        "salut",
        "s'il",
        "faut",
        "avec",
        "pour",
        "vous",
        "nous",
        "etre",
        "être"
    ];

    public static TextToSpeechVoiceSelection Resolve(
        TextToSpeechToolOptions options,
        Dictionary<string, object> parameters,
        string text)
    {
        var requestedLanguage = GetString(parameters, "language");
        var autoDetected = false;

        if (string.IsNullOrWhiteSpace(requestedLanguage) && options.EnableLanguageVoiceSelection)
        {
            requestedLanguage = DetectLanguageFromText(text);
            autoDetected = !string.IsNullOrWhiteSpace(requestedLanguage);
        }

        var normalizedLanguage = NormalizeLanguageTag(requestedLanguage);
        var isFrench = IsFrenchLanguage(normalizedLanguage);

        var selectedVoicePath = options.PiperVoicePath;
        var selectedSpeakerId = options.PiperSpeakerId;

        if (options.EnableLanguageVoiceSelection && isFrench)
        {
            if (!string.IsNullOrWhiteSpace(options.FrenchPiperVoicePath))
            {
                selectedVoicePath = options.FrenchPiperVoicePath;
            }

            if (options.FrenchPiperSpeakerId.HasValue)
            {
                selectedSpeakerId = options.FrenchPiperSpeakerId.Value;
            }
        }

        return new TextToSpeechVoiceSelection(
            LanguageTag: normalizedLanguage,
            PiperVoicePath: selectedVoicePath,
            SpeakerId: selectedSpeakerId,
            AutoDetectedLanguage: autoDetected,
            IsFrench: isFrench);
    }

    private static string NormalizeLanguageTag(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized.Replace('_', '-');
    }

    private static bool IsFrenchLanguage(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return false;
        }

        return languageTag == "fr" || languageTag.StartsWith("fr-", StringComparison.Ordinal);
    }

    private static string DetectLanguageFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains('é') || lower.Contains('è') || lower.Contains('à') || lower.Contains('ç'))
        {
            return "fr";
        }

        foreach (var hint in FrenchHints)
        {
            if (lower.Contains(hint, StringComparison.Ordinal))
            {
                return "fr";
            }
        }

        return string.Empty;
    }

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString()
        };
    }
}
