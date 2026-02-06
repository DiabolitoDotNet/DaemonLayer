using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class ScriptedLlmClient : ILlmClient
{
    private static readonly Regex NameRegex = new(@"^Name:\s*(.+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _scripts = new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(string agentName, params string[] responses)
    {
        var queue = _scripts.GetOrAdd(agentName, _ => new ConcurrentQueue<string>());
        foreach (var response in responses)
        {
            queue.Enqueue(response);
        }
    }

    public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var agentName = TryExtractAgentName(userMessage) ?? "default";

        if (_scripts.TryGetValue(agentName, out var queue) && queue.TryDequeue(out var response))
        {
            return Task.FromResult(response);
        }

        if (_scripts.TryGetValue("default", out var defaultQueue) && defaultQueue.TryDequeue(out var defaultResponse))
        {
            return Task.FromResult(defaultResponse);
        }

        // Safe fallback: terminate quickly.
        return Task.FromResult("{\"thought\":\"No script available; returning fallback\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"(test fallback)\"}");
    }

    public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult("(test simple completion)");

    private static string? TryExtractAgentName(string userMessage)
    {
        var m = NameRegex.Match(userMessage);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
