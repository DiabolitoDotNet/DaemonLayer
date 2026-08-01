using Microsoft.Extensions.Configuration;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostConfigurationBinding
{
    public static T ConfigureAndRead<T>(WebApplicationBuilder builder, string sectionName) where T : class, new()
    {
        builder.Services.Configure<T>(builder.Configuration.GetSection(sectionName));
        return Read<T>(builder.Configuration, sectionName);
    }

    public static T Read<T>(IConfiguration configuration, string sectionName) where T : new()
    {
        return configuration.GetSection(sectionName).Get<T>() ?? new T();
    }
}