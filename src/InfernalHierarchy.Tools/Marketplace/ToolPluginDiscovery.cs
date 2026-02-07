using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace InfernalHierarchy.Tools.Marketplace;

public static class ToolPluginDiscovery
{
    public static IReadOnlyList<Type> DiscoverToolTypes(Assembly assembly)
    {
        return assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ITool).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) != null || t.GetConstructors().Length > 0)
            .ToList();
    }

    public static IReadOnlyList<ITool> CreateTools(
        Assembly assembly,
        IServiceProvider services,
        ILogger logger)
    {
        var tools = new List<ITool>();
        var types = DiscoverToolTypes(assembly);

        foreach (var type in types)
        {
            try
            {
                var instance = ActivatorUtilities.CreateInstance(services, type);
                if (instance is ITool tool)
                {
                    tools.Add(tool);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create tool from plugin type {Type}", type.FullName);
            }
        }

        return tools;
    }
}
