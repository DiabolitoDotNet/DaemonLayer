using System.Globalization;
using System.Text;

namespace InfernalHierarchy.Core.Utilities;

public static class KeyNormalization
{
    public static string NormalizePersonaKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();

        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);

            if (char.IsLetterOrDigit(lower))
            {
                builder.Append(lower);
                continue;
            }

            if (lower is '_' or '-')
            {
                builder.Append('_');
                continue;
            }

            if (char.IsWhiteSpace(lower))
            {
                builder.Append('_');
            }
        }

        // Collapse duplicate underscores
        var normalized = builder.ToString();
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Trim('_');
    }
}
