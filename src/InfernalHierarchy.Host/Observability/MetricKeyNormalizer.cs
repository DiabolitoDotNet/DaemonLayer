using System.Text;

namespace InfernalHierarchy.Host.Observability;

internal static class MetricKeyNormalizer
{
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var trimmed = value.Trim();
        if (trimmed == "/")
        {
            return "root";
        }

        trimmed = trimmed.Trim('/');
        if (trimmed.Length == 0)
        {
            return "root";
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            switch (c)
            {
                case '/':
                case '.':
                case '-':
                case ' ':
                    sb.Append('.');
                    break;
                case '{':
                case '}':
                case '[':
                case ']':
                case '(':
                case ')':
                    // strip grouping chars (helps keep route templates low-cardinality)
                    break;
                default:
                    sb.Append('_');
                    break;
            }
        }

        var s = sb.ToString();
        while (s.Contains("..", StringComparison.Ordinal))
        {
            s = s.Replace("..", ".", StringComparison.Ordinal);
        }

        return s.Trim('.', '_');
    }
}
