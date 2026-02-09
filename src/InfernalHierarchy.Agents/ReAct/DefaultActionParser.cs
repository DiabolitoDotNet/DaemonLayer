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
        var action = NormalizeActionName(ExtractSection(response, "Action"));
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
            var action = root.TryGetProperty("action", out var actionProp) ? NormalizeActionName(actionProp.GetString() ?? string.Empty) : null;
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var thought = root.TryGetProperty("thought", out var thoughtProp) ? thoughtProp.GetString() ?? string.Empty : string.Empty;

            string actionInputText = string.Empty;
            Dictionary<string, object>? actionInputObject = null;

            if (!TryGetAnyProperty(root, out var inputProp,
                    "actionInput",
                    "action_input",
                    "actionInputText",
                    "action_input_text",
                    "finalAnswer",
                    "final_answer",
                    "answer"))
            {
                inputProp = default;
            }

            if (inputProp.ValueKind != JsonValueKind.Undefined)
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

    private static string NormalizeActionName(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return string.Empty;
        }

        var trimmed = action.Trim();
        trimmed = trimmed.Trim('`', '*', '_', '-', '>', '"', '\'', ' ', '\t');

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var tokens = trimmed
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('`', '*', '_', '-', '>', '"', '\'', '.', ':', ';', ',', '!', '?', '(', ')', '[', ']', '{', '}'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        // Prefer an identifier-like token that matches our tool naming convention (underscored).
        foreach (var token in tokens)
        {
            if (token.Equals("FINAL_ANSWER", StringComparison.OrdinalIgnoreCase))
            {
                return "FINAL_ANSWER";
            }

            if (token.Contains('_', StringComparison.Ordinal) && Regex.IsMatch(token, "^[A-Za-z][A-Za-z0-9_]*$"))
            {
                return token;
            }
        }

        // Fallback: first identifier-ish token.
        foreach (var token in tokens)
        {
            if (Regex.IsMatch(token, "^[A-Za-z][A-Za-z0-9_]*$"))
            {
                return token;
            }
        }

        return tokens[0];
    }

    private static bool TryGetAnyProperty(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}
