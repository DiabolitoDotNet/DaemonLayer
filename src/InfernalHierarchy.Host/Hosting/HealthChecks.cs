using Microsoft.Extensions.Diagnostics.HealthChecks;
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
/// Health check for ONNX embeddings assets (local model + tokenizer)
/// </summary>
public class OnnxEmbeddingsHealthCheck : IHealthCheck
{
    private readonly OnnxEmbeddingOptions _options;
    private readonly InfernalHierarchy.Memory.Embeddings.OnnxEmbeddingService _embeddingService;

    public OnnxEmbeddingsHealthCheck(
        IOptions<OnnxEmbeddingOptions> options,
        InfernalHierarchy.Memory.Embeddings.OnnxEmbeddingService embeddingService)
    {
        _options = options.Value;
        _embeddingService = embeddingService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["enabled"] = _options.Enabled,
            ["model_path"] = _options.ModelPath,
            ["tokenizer_path"] = _options.TokenizerPath,
            ["embedding_dimension"] = _options.EmbeddingDimension,
            ["max_sequence_length"] = _options.MaxSequenceLength,
        };

        if (!_options.Enabled)
        {
            data["status"] = "disabled";
            return HealthCheckResult.Healthy("ONNX embeddings disabled", data: data);
        }

        var modelExists = !string.IsNullOrWhiteSpace(_options.ModelPath) && File.Exists(_options.ModelPath);
        var tokenizerExists = !string.IsNullOrWhiteSpace(_options.TokenizerPath) && File.Exists(_options.TokenizerPath);

        data["model_exists"] = modelExists;
        data["tokenizer_exists"] = tokenizerExists;

        if (!modelExists || !tokenizerExists)
        {
            data["status"] = "missing_assets";
            return HealthCheckResult.Degraded("ONNX embeddings enabled but model/tokenizer assets are missing", data: data);
        }

        try
        {
            var probe = await _embeddingService.ProbeAsync(cancellationToken).ConfigureAwait(false);
            data["model_loaded"] = probe.ModelLoaded;
            data["tokenizer_loaded"] = probe.TokenizerLoaded;
            data["using_fallback"] = probe.UsingFallback;

            if (probe.ModelLoaded && probe.TokenizerLoaded)
            {
                data["status"] = "ready";
                return HealthCheckResult.Healthy("ONNX embeddings loaded", data: data);
            }

            data["status"] = "fallback";
            return HealthCheckResult.Degraded("ONNX embeddings enabled but runtime loaded fallback (model/tokenizer failed to initialize)", data: data);
        }
        catch (Exception ex)
        {
            data["status"] = "probe_failed";
            return HealthCheckResult.Degraded("ONNX embeddings probe failed", ex, data);
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
    private readonly ResourceLimits _resourceLimits;
    private readonly MetricsCollector _metrics;

    public LiteDbHealthCheck(
        ISharedMemory sharedMemory,
        IOptions<MemoryOptions> options,
        ResourceLimits resourceLimits,
        MetricsCollector metrics)
    {
        _sharedMemory = sharedMemory;
        _options = options.Value;
        _resourceLimits = resourceLimits;
        _metrics = metrics;
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
            var sizeBytes = exists ? fileInfo.Length : 0L;
            var sizeKb = exists ? fileInfo.Length / 1024 : 0;
            var maxBytes = Math.Max(1L, _resourceLimits.MaxDatabaseSizeBytes);
            var usageRatio = exists ? (double)sizeBytes / maxBytes : 0d;
            var sizeStatus = !exists
                ? "missing"
                : usageRatio >= 1d
                    ? "critical"
                    : usageRatio >= 0.85d
                        ? "warning"
                        : "normal";

            _metrics.SetGauge("memory.database.size.bytes", sizeBytes);

            var data = new Dictionary<string, object>
            {
                ["database_path"] = dbPath,
                ["database_exists"] = exists,
                ["database_size_bytes"] = sizeBytes,
                ["database_size_kb"] = sizeKb,
                ["max_database_size_bytes"] = maxBytes,
                ["database_usage_ratio"] = usageRatio,
                ["database_size_status"] = sizeStatus,
                ["status"] = "operational"
            };

            if (exists && usageRatio >= 1d)
            {
                return HealthCheckResult.Unhealthy(
                    "LiteDB size exceeded configured limit",
                    data: data);
            }

            if (exists && usageRatio >= 0.85d)
            {
                return HealthCheckResult.Degraded(
                    "LiteDB size approaching configured limit",
                    data: data);
            }

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

/// <summary>
/// Health check for optional voice sidecar used by STT/TTS tools.
/// </summary>
public class VoiceSidecarHealthCheck : IHealthCheck
{
    private readonly VoiceTranscriptionToolOptions _stt;
    private readonly TextToSpeechToolOptions _tts;
    private readonly IHttpClientFactory _httpClientFactory;

    public VoiceSidecarHealthCheck(
        IOptions<VoiceTranscriptionToolOptions> stt,
        IOptions<TextToSpeechToolOptions> tts,
        IHttpClientFactory httpClientFactory)
    {
        _stt = stt.Value;
        _tts = tts.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sidecarEnabled = _stt.Enabled && _stt.UseSidecar || _tts.Enabled && _tts.UseSidecar;
        if (!sidecarEnabled)
        {
            return HealthCheckResult.Healthy("Voice sidecar not enabled");
        }

        var baseUrl = _stt.UseSidecar ? _stt.SidecarBaseUrl : _tts.SidecarBaseUrl;
        var healthEndpoint = new Uri(baseUrl, "/health");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            var response = await client.GetAsync(healthEndpoint, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Voice sidecar reachable", data: new Dictionary<string, object>
                {
                    ["url"] = baseUrl.ToString(),
                    ["health_endpoint"] = healthEndpoint.ToString(),
                    ["status"] = "connected"
                });
            }

            return HealthCheckResult.Degraded($"Voice sidecar returned {(int)response.StatusCode}", data: new Dictionary<string, object>
            {
                ["url"] = baseUrl.ToString(),
                ["health_endpoint"] = healthEndpoint.ToString(),
                ["status"] = "error"
            });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Voice sidecar unreachable", ex, data: new Dictionary<string, object>
            {
                ["url"] = baseUrl.ToString(),
                ["health_endpoint"] = healthEndpoint.ToString(),
                ["status"] = "unreachable"
            });
        }
    }
}
