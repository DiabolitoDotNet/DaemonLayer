using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Telegram.Bot;
using InfernalHierarchy.Host.Telegram;

namespace InfernalHierarchy.Host.Hosting;

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

            var baseUrl = _options.BaseUrl.ToString().TrimEnd('/');
            var healthUrl = baseUrl.Replace("/v1", "") + "/api/tags"; // Ollama health endpoint

            var response = await httpClient.GetAsync(healthUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var data = new Dictionary<string, object>
                {
                    ["url"] = _options.BaseUrl.ToString(),
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
/// Health check for Qdrant (vector database)
/// </summary>
public class QdrantHealthCheck : IHealthCheck
{
    private readonly VectorMemoryOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public QdrantHealthCheck(IOptions<VectorMemoryOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["enabled"] = _options.Enabled,
            ["url"] = _options.QdrantUrl.ToString(),
            ["collection"] = _options.CollectionName,
            ["vector_dimensions"] = _options.VectorDimensions,
        };

        if (!_options.Enabled)
        {
            return HealthCheckResult.Healthy("Qdrant health check disabled (vector memory disabled)", data: data);
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var collectionsUrl = new Uri(_options.QdrantUrl.ToString().TrimEnd('/') + "/collections");
            var response = await httpClient.GetAsync(collectionsUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                data["status"] = "connected";
                return HealthCheckResult.Healthy("Qdrant is accessible", data: data);
            }

            data["status"] = "error";
            data["http_status"] = (int)response.StatusCode;
            return HealthCheckResult.Degraded($"Qdrant returned {response.StatusCode}", data: data);
        }
        catch (HttpRequestException ex)
        {
            data["status"] = "unreachable";
            return HealthCheckResult.Unhealthy("Cannot connect to Qdrant", ex, data);
        }
        catch (TaskCanceledException)
        {
            data["status"] = "timeout";
            return HealthCheckResult.Unhealthy("Qdrant health check timed out", data: data);
        }
        catch (Exception ex)
        {
            data["status"] = "failed";
            return HealthCheckResult.Unhealthy("Qdrant health check failed", ex, data);
        }
    }
}

/// <summary>
/// Health check for Telegram Bot service
/// </summary>
public class TelegramHealthCheck : IHealthCheck
{
    private readonly TelegramOptions _options;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public TelegramHealthCheck(IOptions<TelegramOptions> options, ITelegramBotClientFactory botClientFactory)
    {
        _options = options.Value;
        _botClientFactory = botClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return HealthCheckResult.Degraded("Telegram bot token not configured");
        }

        try
        {
            var botClient = _botClientFactory.Create(_options.BotToken);
            var me = await botClient.GetMeAsync(cancellationToken);

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
