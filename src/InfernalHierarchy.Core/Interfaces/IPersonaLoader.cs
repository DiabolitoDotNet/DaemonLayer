
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Loads agent personas from JSON files
/// </summary>
public interface IPersonaLoader
{
    Task<Persona?> LoadPersonaAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<Persona>> LoadAllPersonasAsync(CancellationToken ct = default);
    Task<bool> ValidatePersonaAsync(string name, CancellationToken ct = default);
}
