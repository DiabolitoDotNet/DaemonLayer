using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration;

public sealed class BraveSearchDockerSecretsPostConfigureOptions : IPostConfigureOptions<BraveSearchOptions>
{
    private const string DefaultDockerSecretsRoot = "/run/secrets";
    private const string ApiKeySecretName = "brave_search_api_key";

    private readonly IConfiguration _configuration;
    private readonly ILogger<BraveSearchDockerSecretsPostConfigureOptions> _logger;

    public BraveSearchDockerSecretsPostConfigureOptions(
        IConfiguration configuration,
        ILogger<BraveSearchDockerSecretsPostConfigureOptions> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PostConfigure(string? name, BraveSearchOptions options)
    {
        if (_configuration.GetValue<bool?>("DockerSecrets:Enabled") is false)
        {
            return;
        }

        if (!options.Enabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        var rootPath = _configuration["DockerSecrets:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = DefaultDockerSecretsRoot;
        }

        var path = Path.Combine(rootPath, ApiKeySecretName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var apiKey = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            options.ApiKey = apiKey;
            _logger.LogInformation("Loaded BraveSearch api key from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read BraveSearch api key from docker secret {Path}", path);
        }
    }
}
