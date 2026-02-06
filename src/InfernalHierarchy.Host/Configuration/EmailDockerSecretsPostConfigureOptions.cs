using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InfernalHierarchy.Host.Configuration;

public sealed class EmailDockerSecretsPostConfigureOptions : IPostConfigureOptions<EmailNotificationOptions>
{
    private const string DefaultDockerSecretsRoot = "/run/secrets";
    private const string SecretName = "email_smtp_json";

    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailDockerSecretsPostConfigureOptions> _logger;

    public EmailDockerSecretsPostConfigureOptions(
        IConfiguration configuration,
        ILogger<EmailDockerSecretsPostConfigureOptions> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PostConfigure(string? name, EmailNotificationOptions options)
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

        var path = Path.Combine(rootPath, SecretName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            var secret = JsonSerializer.Deserialize<EmailSecret>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (secret is null)
            {
                _logger.LogWarning("Failed to parse Email SMTP secret JSON from {Path}", path);
                return;
            }

            // Apply values (secret overrides defaults; doesn't clobber explicitly configured values unless they are empty/default)
            if (secret.Enabled.HasValue)
            {
                options.Enabled = secret.Enabled.Value;
            }

            if (!string.IsNullOrWhiteSpace(secret.Host) && string.IsNullOrWhiteSpace(options.Host))
            {
                options.Host = secret.Host;
            }

            if (secret.Port.HasValue && options.Port == 587)
            {
                options.Port = secret.Port.Value;
            }

            if (secret.UseSsl.HasValue)
            {
                options.UseSsl = secret.UseSsl.Value;
            }

            if (!string.IsNullOrWhiteSpace(secret.Username) && string.IsNullOrWhiteSpace(options.Username))
            {
                options.Username = secret.Username;
            }

            if (!string.IsNullOrWhiteSpace(secret.Password) && string.IsNullOrWhiteSpace(options.Password))
            {
                options.Password = secret.Password;
            }

            if (!string.IsNullOrWhiteSpace(secret.FromAddress) && string.IsNullOrWhiteSpace(options.FromAddress))
            {
                options.FromAddress = secret.FromAddress;
            }

            if (secret.FromName is not null && options.FromName is null)
            {
                options.FromName = string.IsNullOrWhiteSpace(secret.FromName) ? null : secret.FromName;
            }

            if (secret.TimeoutMs.HasValue && options.TimeoutMs == 15_000)
            {
                options.TimeoutMs = secret.TimeoutMs.Value;
            }

            _logger.LogInformation("Loaded Email SMTP settings from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Email SMTP settings from docker secret {Path}", path);
        }
    }

    private sealed class EmailSecret
    {
        public bool? Enabled { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public bool? UseSsl { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
        public int? TimeoutMs { get; set; }
    }
}
