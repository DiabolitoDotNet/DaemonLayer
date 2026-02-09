using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration;

public sealed class GitHubPublisherDockerSecretsPostConfigureOptions : IPostConfigureOptions<GitHubPublisherOptions>
{
    private const string DefaultDockerSecretsRoot = "/run/secrets";
    private const string TokenSecretName = "github_publisher_token";
    private const string UsernameSecretName = "github_publisher_username";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubPublisherDockerSecretsPostConfigureOptions> _logger;

    public GitHubPublisherDockerSecretsPostConfigureOptions(
        IConfiguration configuration,
        ILogger<GitHubPublisherDockerSecretsPostConfigureOptions> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PostConfigure(string? name, GitHubPublisherOptions options)
    {
        if (_configuration.GetValue<bool?>("DockerSecrets:Enabled") is false)
        {
            return;
        }

        var rootPath = _configuration["DockerSecrets:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = DefaultDockerSecretsRoot;
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            TryLoadTokenFromSecret(rootPath, options);
        }

        if (string.IsNullOrWhiteSpace(options.Username) && string.IsNullOrWhiteSpace(options.Owner))
        {
            TryLoadUsernameFromSecret(rootPath, options);
        }
    }

    private void TryLoadTokenFromSecret(string rootPath, GitHubPublisherOptions options)
    {
        var path = Path.Combine(rootPath, TokenSecretName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var token = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            options.Token = token;
            _logger.LogInformation("Loaded GitHub publisher token from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GitHub publisher token from docker secret {Path}", path);
        }
    }

    private void TryLoadUsernameFromSecret(string rootPath, GitHubPublisherOptions options)
    {
        var path = Path.Combine(rootPath, UsernameSecretName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var username = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            options.Username = username;
            if (string.IsNullOrWhiteSpace(options.Owner))
            {
                options.Owner = username;
            }

            _logger.LogInformation("Loaded GitHub publisher username from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GitHub publisher username from docker secret {Path}", path);
        }
    }
}
