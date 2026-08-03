using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

internal sealed record SensitiveInputAssessment(
    bool ContainsSensitiveCredentials,
    bool HasSecretReference,
    string ReasonCode)
{
    public bool RequiresSecretReference => ContainsSensitiveCredentials && !HasSecretReference;
}

internal static class SensitiveInputGuard
{
    private static readonly Regex SensitiveCredentialRegex = new(
        @"\b(password|passwd|pwd|token|api[_\s-]?key|secret|credentials?|login)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretReferenceRegex = new(
        @"\b(secret://|vault://|env://|ref://)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SensitiveInputAssessment Assess(AgentMessage task)
    {
        var content = task.Content ?? string.Empty;
        var payloadText = task.Payload is null
            ? string.Empty
            : string.Join(' ', task.Payload.Values.Select(v => v?.ToString() ?? string.Empty));

        var combined = string.IsNullOrWhiteSpace(payloadText)
            ? content
            : $"{content} {payloadText}";

        var containsSensitive = SensitiveCredentialRegex.IsMatch(combined);
        var hasSecretReference = SecretReferenceRegex.IsMatch(combined);

        return new SensitiveInputAssessment(
            ContainsSensitiveCredentials: containsSensitive,
            HasSecretReference: hasSecretReference,
            ReasonCode: containsSensitive && !hasSecretReference
                ? "secret_reference_required"
                : "none");
    }
}
