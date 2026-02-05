using System.Globalization;
using System.Text;

namespace InfernalHierarchy.Host.Observability;

/// <summary>
/// Formats internal metrics to Prometheus text exposition format.
/// </summary>
public static class PrometheusMetricsFormatter
{
    /// <summary>
    /// Formats the given metrics dictionary to Prometheus text format.
    /// </summary>
    public static string Format(Dictionary<string, object> metrics, string prefix = "infernal")
    {
        if (metrics == null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        var sb = new StringBuilder(capacity: Math.Max(1024, metrics.Count * 64));
        sb.AppendLine("# Prometheus exposition format");
        sb.Append("# Generated at ")
            .Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
            .AppendLine();

        foreach (var (key, value) in metrics.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (TryFormatMetricLine(prefix, key, value, out var line))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private static bool TryFormatMetricLine(string prefix, string key, object value, out string line)
    {
        line = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var metricName = key;

        if (key.StartsWith("counter.", StringComparison.OrdinalIgnoreCase))
        {
            metricName = key["counter.".Length..] + "_total";
        }
        else if (key.StartsWith("gauge.", StringComparison.OrdinalIgnoreCase))
        {
            metricName = key["gauge.".Length..];
        }
        else if (key.StartsWith("histogram.", StringComparison.OrdinalIgnoreCase))
        {
            // Our internal "histogram" is exported as gauges (count/mean/p95), not a Prometheus histogram.
            metricName = "histogram." + key["histogram.".Length..];
        }

        metricName = SanitizeMetricName(prefix, metricName);

        if (!TryConvertToDouble(value, out var numericValue))
        {
            return false;
        }

        // NOTE: We intentionally omit HELP/TYPE lines to keep output compact and stable.
        line = string.Create(CultureInfo.InvariantCulture, $"{metricName} {numericValue}");
        return true;
    }

    private static string SanitizeMetricName(string prefix, string name)
    {
        prefix = string.IsNullOrWhiteSpace(prefix) ? "infernal" : prefix;
        name = string.IsNullOrWhiteSpace(name) ? "unknown" : name;

        var sb = new StringBuilder(prefix.Length + name.Length + 2);
        sb.Append(prefix);
        sb.Append('_');

        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == ':')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        // Prometheus metric names must match [a-zA-Z_:][a-zA-Z0-9_:]*
        if (sb.Length == 0)
        {
            return "infernal_unknown";
        }

        var first = sb[0];
        if (!(char.IsLetter(first) || first == '_' || first == ':'))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        switch (value)
        {
            case byte v:
                result = v;
                return true;
            case sbyte v:
                result = v;
                return true;
            case short v:
                result = v;
                return true;
            case ushort v:
                result = v;
                return true;
            case int v:
                result = v;
                return true;
            case uint v:
                result = v;
                return true;
            case long v:
                result = v;
                return true;
            case ulong v:
                result = v;
                return true;
            case float v:
                result = v;
                return true;
            case double v:
                result = v;
                return true;
            case decimal v:
                result = (double)v;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
