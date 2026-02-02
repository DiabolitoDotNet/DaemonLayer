using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Telegram.Bot;

namespace InfernalHierarchy.Host;

/// <summary>
/// Health check for Ollama LLM service
/// </summary>
public class OllamaHealthCheck : IHealthCheck
{
    private readonly OllamaOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public OllamaHealthCheck(IOptions<OllamaOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var healthUrl = baseUrl.Replace("/v1", "") + "/api/tags"; // Ollama health endpoint

            var response = await httpClient.GetAsync(healthUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var data = new Dictionary<string, object>
                {
                    ["url"] = _options.BaseUrl,
                    ["model"] = _options.DefaultModel,
                    ["status"] = "connected"
                };

                return HealthCheckResult.Healthy("Ollama is accessible", data);
            }

            return HealthCheckResult.Degraded($"Ollama returned {response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy("Cannot connect to Ollama", ex);
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Ollama health check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ollama health check failed", ex);
        }
    }
}

/// <summary>
/// Health check for Telegram Bot service
/// </summary>
public class TelegramHealthCheck : IHealthCheck
{
    private readonly TelegramOptions _options;

    public TelegramHealthCheck(IOptions<TelegramOptions> options)
    {
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return HealthCheckResult.Degraded("Telegram bot token not configured");
        }

        try
        {
            var botClient = new TelegramBotClient(_options.BotToken);
            var me = await botClient.GetMe(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["bot_username"] = me.Username ?? "unknown",
                ["bot_id"] = me.Id,
                ["status"] = "connected"
            };

            return HealthCheckResult.Healthy($"Telegram bot @{me.Username} is active", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot connect to Telegram", ex);
        }
    }
}

/// <summary>
/// Health check for LiteDB shared memory
/// </summary>
public class LiteDbHealthCheck : IHealthCheck
{
    private readonly ISharedMemory _sharedMemory;
    private readonly MemoryOptions _options;

    public LiteDbHealthCheck(ISharedMemory sharedMemory, IOptions<MemoryOptions> options)
    {
        _sharedMemory = sharedMemory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try a simple read operation
            var recentDecisions = await _sharedMemory.GetRecentDecisionsAsync(1, cancellationToken);

            var dbPath = _options.DatabasePath;
            var fileInfo = new FileInfo(dbPath);
            var exists = fileInfo.Exists;
            var sizeKb = exists ? fileInfo.Length / 1024 : 0;

            var data = new Dictionary<string, object>
            {
                ["database_path"] = dbPath,
                ["database_exists"] = exists,
                ["database_size_kb"] = sizeKb,
                ["status"] = "operational"
            };

            return HealthCheckResult.Healthy("LiteDB is accessible", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("LiteDB health check failed", ex);
        }
    }
}

/// <summary>
/// Health check for agent hierarchy status
/// </summary>
public class AgentHierarchyHealthCheck : IHealthCheck
{
    private readonly IAgentFactory _agentFactory;

    public AgentHierarchyHealthCheck(IAgentFactory agentFactory)
    {
        _agentFactory = agentFactory;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var allAgents = _agentFactory.GetAllAgents().ToList();
            var totalAgents = allAgents.Count;

            if (totalAgents == 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded("No agents are currently running"));
            }

            var data = new Dictionary<string, object>
            {
                ["total_agents"] = totalAgents,
                ["supreme_count"] = allAgents.Count(a => a.Rank == Core.Entities.AgentRank.Supreme),
                ["prince_count"] = allAgents.Count(a => a.Rank == Core.Entities.AgentRank.Prince),
                ["duke_count"] = allAgents.Count(a => a.Rank == Core.Entities.AgentRank.Duke),
                ["worker_count"] = allAgents.Count(a => a.Rank == Core.Entities.AgentRank.Worker),
                ["idle_count"] = allAgents.Count(a => a.Status == Core.Entities.AgentStatus.Idle),
                ["active_count"] = allAgents.Count(a => a.Status != Core.Entities.AgentStatus.Idle)
            };

            return Task.FromResult(HealthCheckResult.Healthy($"{totalAgents} agent(s) running", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Agent hierarchy check failed", ex));
        }
    }
}
