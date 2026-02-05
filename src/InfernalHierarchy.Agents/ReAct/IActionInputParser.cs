namespace InfernalHierarchy.Agents.ReAct;

public interface IActionInputParser
{
    Dictionary<string, object> Parse(string input, string actionName);
}
