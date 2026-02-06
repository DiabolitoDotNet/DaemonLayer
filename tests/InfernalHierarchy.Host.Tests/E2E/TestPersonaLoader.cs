using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class TestPersonaLoader : IPersonaLoader
{
    public Task<Persona?> LoadPersonaAsync(string name, CancellationToken ct = default)
    {
        var normalized = name.Trim();

        // Minimal personas for deterministic E2E tests.
        var persona = normalized.ToLowerInvariant() switch
        {
            "lucifer" => new Persona
            {
                Name = "Lucifer",
                DemonTitle = "Supreme Agent",
                SystemPrompt = "You are Lucifer (test persona).",
                Specializations = new List<string> { "testing" },
                AvailableTools = new List<string>
                {
                    "web_search",
                    "email_send",
                    "create_sub_agent",
                    "request_collaboration"
                }
            },
            "baal" => new Persona
            {
                Name = "Baal",
                DemonTitle = "Prince",
                SystemPrompt = "You are Baal (test persona).",
                Specializations = new List<string> { "analysis" },
                AvailableTools = new List<string>()
            },
            "vassago" => new Persona
            {
                Name = "Vassago",
                DemonTitle = "Duke",
                SystemPrompt = "You are Vassago (test persona).",
                Specializations = new List<string> { "research" },
                AvailableTools = new List<string>()
            },
            _ => new Persona
            {
                Name = normalized,
                DemonTitle = "Agent",
                SystemPrompt = $"You are {normalized} (test persona).",
                Specializations = new List<string> { "testing" },
                AvailableTools = new List<string>()
            }
        };

        return Task.FromResult<Persona?>(persona);
    }

    public Task<IEnumerable<Persona>> LoadAllPersonasAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<Persona>>(new[]
        {
            new Persona { Name = "Lucifer" },
            new Persona { Name = "Baal" },
            new Persona { Name = "Vassago" }
        });

    public Task<bool> ValidatePersonaAsync(string name, CancellationToken ct = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(name));
}
