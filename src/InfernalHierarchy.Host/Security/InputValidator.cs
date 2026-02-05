using System.Text.RegularExpressions;

namespace InfernalHierarchy.Host.Security;

/// <summary>
/// Input validation and sanitization utilities
/// </summary>
public static class InputValidator
{
    private static readonly Regex SqlInjectionPattern = new(@"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT( +INTO)?|MERGE|SELECT|UPDATE|UNION( +ALL)?)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XssPattern = new(@"<script[^>]*>.*?</script>|javascript:|onerror=|onload=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandInjectionPattern = new(@"[;&|`$\(\)]",
        RegexOptions.Compiled);

    /// <summary>
    /// Sanitize user input for safe processing
    /// </summary>
    public static string SanitizeInput(string input, int maxLength = 10000)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Truncate to max length
        if (input.Length > maxLength)
            input = input[..maxLength];

        // Remove null characters
        input = input.Replace("\0", "");

        // Trim whitespace
        input = input.Trim();

        return input;
    }

    /// <summary>
    /// Validate that input doesn't contain SQL injection attempts
    /// </summary>
    public static bool IsSafeSql(string input)
    {
        if (string.IsNullOrEmpty(input))
            return true;

        return !SqlInjectionPattern.IsMatch(input);
    }

    /// <summary>
    /// Validate that input doesn't contain XSS attempts
    /// </summary>
    public static bool IsSafeXss(string input)
    {
        if (string.IsNullOrEmpty(input))
            return true;

        return !XssPattern.IsMatch(input);
    }

    /// <summary>
    /// Validate that input doesn't contain command injection attempts
    /// </summary>
    public static bool IsSafeCommand(string input)
    {
        if (string.IsNullOrEmpty(input))
            return true;

        return !CommandInjectionPattern.IsMatch(input);
    }

    /// <summary>
    /// Comprehensive validation for user input
    /// </summary>
    public static ValidationResult ValidateUserInput(string input, int maxLength = 10000)
    {
        if (string.IsNullOrEmpty(input))
            return new ValidationResult { IsValid = true, SanitizedValue = string.Empty };

        var sanitized = SanitizeInput(input, maxLength);

        var issues = new List<string>();

        if (!IsSafeSql(sanitized))
            issues.Add("Potential SQL injection detected");

        if (!IsSafeXss(sanitized))
            issues.Add("Potential XSS detected");

        if (!IsSafeCommand(sanitized))
            issues.Add("Potential command injection detected");

        return new ValidationResult
        {
            IsValid = issues.Count == 0,
            SanitizedValue = sanitized,
            ValidationIssues = issues
        };
    }

    /// <summary>
    /// Validate agent name follows naming conventions
    /// </summary>
    public static bool IsValidAgentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Allow letters, numbers, spaces, hyphens, and underscores
        return Regex.IsMatch(name, @"^[a-zA-Z0-9\s\-_]{1,50}$");
    }

    /// <summary>
    /// Validate file path is safe (no directory traversal)
    /// </summary>
    public static bool IsValidFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for directory traversal attempts
        if (path.Contains("..") || path.Contains("~"))
            return false;

        // Check for absolute paths
        if (Path.IsPathRooted(path))
            return false;

        return true;
    }

    /// <summary>
    /// Validate URL is safe and uses allowed schemes
    /// </summary>
    public static bool IsValidUrl(string url, string[]? allowedSchemes = null)
    {
        allowedSchemes ??= new[] { "http", "https" };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return allowedSchemes.Contains(uri.Scheme.ToLowerInvariant());
    }

    /// <summary>
    /// Sanitize JSON string for safe parsing
    /// </summary>
    public static string SanitizeJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return "{}";

        // Remove control characters except valid JSON whitespace
        json = Regex.Replace(json, @"[\x00-\x08\x0B-\x0C\x0E-\x1F]", "");

        return json;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string SanitizedValue { get; set; } = string.Empty;
    public List<string> ValidationIssues { get; set; } = new();
}
