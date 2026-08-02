using System.Collections.Concurrent;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Host.Agents;

public sealed class InMemoryAgentSkillRuntimeStore : IAgentSkillRuntimeStore
{
    private readonly ConcurrentDictionary<string, List<AgentSkillGrant>> _grants = new(StringComparer.OrdinalIgnoreCase);

    public void ApplyGrant(string agentId, AgentSkillGrant grant)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent id is required", nameof(agentId));
        }

        ArgumentNullException.ThrowIfNull(grant);

        var list = _grants.GetOrAdd(agentId, _ => new List<AgentSkillGrant>());

        lock (list)
        {
            list.Add(grant);
            list.RemoveAll(g => g.ExpiresAtUtc <= DateTime.UtcNow);
        }
    }

    public AgentSkillRuntimeOverlay GetOverlay(string agentId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return new AgentSkillRuntimeOverlay();
        }

        if (!_grants.TryGetValue(agentId, out var list))
        {
            return new AgentSkillRuntimeOverlay();
        }

        List<AgentSkillGrant> active;
        lock (list)
        {
            list.RemoveAll(g => g.ExpiresAtUtc <= utcNow);
            active = list.ToList();
        }

        if (active.Count == 0)
        {
            return new AgentSkillRuntimeOverlay();
        }

        return new AgentSkillRuntimeOverlay
        {
            ActiveSkillPackIds = active.Select(g => g.SkillPackId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AdditionalTools = active.SelectMany(g => g.AdditionalTools)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AdditionalSpecializations = active.SelectMany(g => g.AdditionalSpecializations)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PromptFragments = active.SelectMany(g => g.PromptFragments)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public int PruneExpired(DateTime utcNow)
    {
        var removed = 0;

        foreach (var kvp in _grants)
        {
            var list = kvp.Value;
            lock (list)
            {
                var before = list.Count;
                list.RemoveAll(g => g.ExpiresAtUtc <= utcNow);
                removed += before - list.Count;
            }
        }

        return removed;
    }
}
