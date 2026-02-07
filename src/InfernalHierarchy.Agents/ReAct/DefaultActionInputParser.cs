using System.Text.Json;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultActionInputParser : IActionInputParser
{
    private readonly ILogger _logger;

    public DefaultActionInputParser(ILogger logger)
    {
        _logger = logger;
    }

    public Dictionary<string, object> Parse(string input, string actionName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            var trimmedInput = input.Trim();
            if (trimmedInput.StartsWith("{", StringComparison.Ordinal) && trimmedInput.EndsWith("}", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(trimmedInput)
                       ?? new Dictionary<string, object>();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("JSON parsing failed: {Error}. Treating as plain text.", ex.Message);
        }

        return new Dictionary<string, object>
        {
            ["query"] = input,
            ["content"] = input,
            ["text"] = input,
            ["message"] = input
        };
    }
}
