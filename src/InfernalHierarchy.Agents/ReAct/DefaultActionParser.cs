using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultActionParser : IActionParser
{
    public bool TryParse(string response, bool useJsonResponse, out ParsedAction parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        if (useJsonResponse && TryParseJsonReActResponse(response, out var jsonParsed))
        {
            parsed = jsonParsed;
            return true;
        }

        var thought = ExtractSection(response, "Thought");
        var action = ExtractSection(response, "Action");
        var actionInput = ExtractSection(response, "Action Input");

        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        parsed = new ParsedAction(
            Thought: thought,
            Action: action,
            ActionInputText: actionInput,
            ActionInputObject: null);

        return true;
    }

    private static string ExtractSection(string text, string sectionName)
    {
        var patterns = new[]
        {
            $@"{sectionName}:\s*(.+?)(?=\n(?:Thought|Action|Observation|---|\Z))",
            $@"{sectionName}\s*:\s*(.+?)(?=\n|$)",
            $@"(?i){sectionName}:\s*(.+?)(?=\n(?:thought|action|observation|---|\Z))"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryParseJsonReActResponse(string response, out ParsedAction parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var candidate = response.Trim();

        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            candidate = Regex.Replace(candidate, "^```[a-zA-Z0-9_-]*\\s*", string.Empty);
            candidate = Regex.Replace(candidate, "\\s*```$", string.Empty);
            candidate = candidate.Trim();
        }

        if (!candidate.StartsWith("{", StringComparison.Ordinal))
        {
            var first = candidate.IndexOf('{');
            var last = candidate.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                candidate = candidate.Substring(first, last - first + 1);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var thought = root.TryGetProperty("thought", out var thoughtProp) ? thoughtProp.GetString() ?? string.Empty : string.Empty;

            string actionInputText = string.Empty;
            Dictionary<string, object>? actionInputObject = null;

            if (root.TryGetProperty("actionInput", out var inputProp))
            {
                if (inputProp.ValueKind == JsonValueKind.Object)
                {
                    actionInputObject = JsonSerializer.Deserialize<Dictionary<string, object>>(inputProp.GetRawText());
                    actionInputText = inputProp.GetRawText();
                }
                else if (inputProp.ValueKind == JsonValueKind.String)
                {
                    actionInputText = inputProp.GetString() ?? string.Empty;
                }
                else
                {
                    actionInputText = inputProp.GetRawText();
                }
            }

            parsed = new ParsedAction(
                Thought: thought.Trim(),
                Action: action.Trim(),
                ActionInputText: actionInputText.Trim(),
                ActionInputObject: actionInputObject);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
