using System.Text.RegularExpressions;

namespace InfernalHierarchy.Tools.Dynamic;

/// <summary>
/// Very conservative static policy for custom tool source.
/// This is not a sandbox; it is a guardrail. Anything that looks like IO/network/process/reflection
/// is flagged as requiring manual approval.
/// </summary>
public sealed class DefaultCustomToolSecurityPolicy : ICustomToolSecurityPolicy
{
    // Any match here triggers manual approval.
    private static readonly (string Rule, Regex Pattern)[] RiskyRules = new[]
    {
        ("System.IO namespace", new Regex(@"\bSystem\.IO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("File/Directory APIs", new Regex(@"\b(File|Directory|Path|FileInfo|DirectoryInfo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Network namespaces", new Regex(@"\bSystem\.(Net|Sockets)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("HttpClient/WebRequest", new Regex(@"\b(HttpClient|HttpRequestMessage|WebRequest|WebClient|Dns|Socket)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Process execution", new Regex(@"\b(System\.Diagnostics\.Process|ProcessStartInfo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Environment access", new Regex(@"\bSystem\.Environment\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Reflection loading", new Regex(@"\b(System\.Reflection|Assembly\.Load|AssemblyLoadContext)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("P/Invoke", new Regex(@"\b(DllImport|System\.Runtime\.InteropServices)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Threading primitives", new Regex(@"\b(System\.Threading\.Thread|ThreadStart)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    // Matches here are denied (too dangerous to even approve in this system).
    private static readonly (string Rule, Regex Pattern)[] DenyRules = new[]
    {
        ("Dynamic IL emit", new Regex(@"\bSystem\.Reflection\.Emit\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("Unsafe code", new Regex(@"\bunsafe\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    public CustomToolPolicyDecision Evaluate(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return new CustomToolPolicyDecision(
                Allowed: false,
                RequiresManualApproval: false,
                Reason: "Source code is empty",
                MatchedRules: Array.Empty<string>());
        }

        var matchedDeny = DenyRules
            .Where(r => r.Pattern.IsMatch(sourceCode))
            .Select(r => r.Rule)
            .ToList();

        if (matchedDeny.Count > 0)
        {
            return new CustomToolPolicyDecision(
                Allowed: false,
                RequiresManualApproval: false,
                Reason: "Custom tool source contains denied constructs",
                MatchedRules: matchedDeny);
        }

        var matchedRisky = RiskyRules
            .Where(r => r.Pattern.IsMatch(sourceCode))
            .Select(r => r.Rule)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchedRisky.Count > 0)
        {
            return new CustomToolPolicyDecision(
                Allowed: true,
                RequiresManualApproval: true,
                Reason: "Custom tool source references risky APIs",
                MatchedRules: matchedRisky);
        }

        return new CustomToolPolicyDecision(
            Allowed: true,
            RequiresManualApproval: false,
            Reason: "OK",
            MatchedRules: Array.Empty<string>());
    }
}
