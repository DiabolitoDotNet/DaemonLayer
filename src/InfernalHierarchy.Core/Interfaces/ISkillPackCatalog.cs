namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Catalog of reusable skill packs.
/// </summary>
public interface ISkillPackCatalog
{
    Task<SkillPack?> GetByIdAsync(string skillPackId, CancellationToken ct = default);

    Task<IReadOnlyList<SkillPack>> GetAllAsync(CancellationToken ct = default);
}
