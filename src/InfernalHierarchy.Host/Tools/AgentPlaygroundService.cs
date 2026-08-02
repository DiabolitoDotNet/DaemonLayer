namespace InfernalHierarchy.Host.Tools;

public interface IAgentPlaygroundService
{
    string CreateScenario(string name, string prompt, string toAgentId, int timeoutMs, Dictionary<string, object>? tags);
    PlaygroundScenario? GetScenario(string id);
    IReadOnlyList<PlaygroundScenario> ListScenarios(int limit);
    PlaygroundRunRecord AddRun(string scenarioId, string prompt, string toAgentId, int timeoutMs, ChatResponse response);
    PlaygroundRunRecord? GetRun(string runId);
    IReadOnlyList<PlaygroundRunRecord> GetRuns(string scenarioId, int limit);
}

public sealed class AgentPlaygroundService : IAgentPlaygroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PlaygroundScenario> _scenarios = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlaygroundRunRecord> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _scenarioOrder = new();
    private readonly List<string> _runOrder = new();

    public string CreateScenario(string name, string prompt, string toAgentId, int timeoutMs, Dictionary<string, object>? tags)
    {
        var id = $"scn_{Guid.NewGuid():N}";
        var scenario = new PlaygroundScenario(
            id,
            name,
            prompt,
            toAgentId,
            timeoutMs,
            DateTime.UtcNow,
            tags ?? new Dictionary<string, object>());

        lock (_gate)
        {
            _scenarios[id] = scenario;
            _scenarioOrder.Add(id);
            if (_scenarioOrder.Count > 1000)
            {
                var oldest = _scenarioOrder[0];
                _scenarioOrder.RemoveAt(0);
                _scenarios.Remove(oldest);
            }
        }

        return id;
    }

    public PlaygroundScenario? GetScenario(string id)
    {
        lock (_gate)
        {
            return _scenarios.TryGetValue(id, out var scenario) ? scenario : null;
        }
    }

    public IReadOnlyList<PlaygroundScenario> ListScenarios(int limit)
    {
        var effectiveLimit = limit <= 0 ? 50 : Math.Min(limit, 500);
        lock (_gate)
        {
            return _scenarioOrder
                .AsEnumerable()
                .Reverse()
                .Take(effectiveLimit)
                .Select(id => _scenarios[id])
                .ToList();
        }
    }

    public PlaygroundRunRecord AddRun(string scenarioId, string prompt, string toAgentId, int timeoutMs, ChatResponse response)
    {
        var run = new PlaygroundRunRecord(
            RunId: $"run_{Guid.NewGuid():N}",
            ScenarioId: scenarioId,
            Prompt: prompt,
            ToAgentId: toAgentId,
            TimeoutMs: timeoutMs,
            Response: response,
            CreatedAtUtc: DateTime.UtcNow);

        lock (_gate)
        {
            _runs[run.RunId] = run;
            _runOrder.Add(run.RunId);
            if (_runOrder.Count > 5000)
            {
                var oldest = _runOrder[0];
                _runOrder.RemoveAt(0);
                _runs.Remove(oldest);
            }
        }

        return run;
    }

    public PlaygroundRunRecord? GetRun(string runId)
    {
        lock (_gate)
        {
            return _runs.TryGetValue(runId, out var run) ? run : null;
        }
    }

    public IReadOnlyList<PlaygroundRunRecord> GetRuns(string scenarioId, int limit)
    {
        var effectiveLimit = limit <= 0 ? 20 : Math.Min(limit, 200);
        lock (_gate)
        {
            return _runOrder
                .AsEnumerable()
                .Reverse()
                .Select(id => _runs[id])
                .Where(r => string.Equals(r.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Take(effectiveLimit)
                .ToList();
        }
    }
}

public sealed record PlaygroundScenario(
    string ScenarioId,
    string Name,
    string Prompt,
    string ToAgentId,
    int TimeoutMs,
    DateTime CreatedAtUtc,
    Dictionary<string, object> Tags);

public sealed record PlaygroundRunRecord(
    string RunId,
    string ScenarioId,
    string Prompt,
    string ToAgentId,
    int TimeoutMs,
    ChatResponse Response,
    DateTime CreatedAtUtc);