using System.Reflection;
using System.Runtime.Loader;

namespace InfernalHierarchy.Tools.Marketplace;

public sealed record ToolPluginLoadResult(
    string PluginPath,
    bool Loaded,
    int ToolCount,
    string? Error);

public interface IToolPluginLoader
{
    Task<(Assembly? Assembly, ToolPluginLoadResult Result)> LoadAssemblyAsync(string pluginPath, CancellationToken ct);
}

public sealed class DefaultToolPluginLoader : IToolPluginLoader
{
    private readonly ILogger<DefaultToolPluginLoader> _logger;

    public DefaultToolPluginLoader(ILogger<DefaultToolPluginLoader> logger)
    {
        _logger = logger;
    }

    public Task<(Assembly? Assembly, ToolPluginLoadResult Result)> LoadAssemblyAsync(string pluginPath, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((
                    null,
                    new ToolPluginLoadResult(pluginPath, Loaded: false, ToolCount: 0, Error: "PluginPath is empty")));
            }

            if (!File.Exists(pluginPath))
            {
                return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((
                    null,
                    new ToolPluginLoadResult(pluginPath, Loaded: false, ToolCount: 0, Error: "Plugin file not found")));
            }

            // Load from a copied path so the original can be updated without file-lock issues.
            var shadowDir = Path.Combine(Path.GetTempPath(), "infernal-plugins");
            Directory.CreateDirectory(shadowDir);

            var shadowPath = Path.Combine(shadowDir, $"{Path.GetFileNameWithoutExtension(pluginPath)}-{Guid.NewGuid():n}.dll");
            File.Copy(pluginPath, shadowPath, overwrite: true);

            var alc = new AssemblyLoadContext($"plugin:{Path.GetFileName(pluginPath)}:{Guid.NewGuid():n}", isCollectible: true);
            var assembly = alc.LoadFromAssemblyPath(shadowPath);

            _logger.LogInformation("📦 Loaded plugin assembly {Assembly} from {Path}", assembly.FullName, pluginPath);

            return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((
                assembly,
                new ToolPluginLoadResult(pluginPath, Loaded: true, ToolCount: 0, Error: null)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin assembly from {Path}", pluginPath);
            return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((
                null,
                new ToolPluginLoadResult(pluginPath, Loaded: false, ToolCount: 0, Error: ex.Message)));
        }
    }
}
