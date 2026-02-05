namespace InfernalHierarchy.Agents.ReAct;

public interface IActionParser
{
    bool TryParse(string response, bool useJsonResponse, out ParsedAction parsed);
}

public readonly record struct ParsedAction(
    string Thought,
    string Action,
    string ActionInputText,
    Dictionary<string, object>? ActionInputObject);
