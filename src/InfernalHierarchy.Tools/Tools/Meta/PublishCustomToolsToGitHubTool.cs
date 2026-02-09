using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Tools.Meta;

/// <summary>
/// Publishes all persisted custom tools (source + definition) to a GitHub private repository.
/// This tool is intended for operator-controlled backups/versioning; it performs network IO.
/// </summary>
public sealed class PublishCustomToolsToGitHubTool : ITool
{
    private static readonly JsonSerializerOptions JsonIndented = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ICustomToolStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GitHubPublisherOptions> _options;
    private readonly ILogger<PublishCustomToolsToGitHubTool> _logger;

    public PublishCustomToolsToGitHubTool(
        ICustomToolStore store,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GitHubPublisherOptions> options,
        ILogger<PublishCustomToolsToGitHubTool> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => "publish_custom_tools_github";

    public string Description =>
        "Upload all persisted custom tools to a private GitHub repo (monorepo). " +
        "Writes definition.json + tool.cs for each tool using GitHub Contents API.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        if (options.Enabled != true)
        {
            return new ToolResult { Success = false, Error = "GitHubPublisher is disabled by configuration" };
        }

        var owner = parameters.GetValueOrDefault("owner")?.ToString();
        var repo = parameters.GetValueOrDefault("repo")?.ToString()
                   ?? parameters.GetValueOrDefault("repository")?.ToString();
        var branch = parameters.GetValueOrDefault("branch")?.ToString();
        var rootPath = parameters.GetValueOrDefault("root_path")?.ToString();

        var configuredOwner = !string.IsNullOrWhiteSpace(options.Owner)
            ? options.Owner
            : options.Username;

        owner = string.IsNullOrWhiteSpace(owner) ? configuredOwner : owner;
        repo = string.IsNullOrWhiteSpace(repo) ? options.Repository : repo;
        branch = string.IsNullOrWhiteSpace(branch) ? options.Branch : branch;
        rootPath = string.IsNullOrWhiteSpace(rootPath) ? options.RootPath : rootPath;

        if (string.IsNullOrWhiteSpace(owner))
        {
            return new ToolResult { Success = false, Error = "Missing required owner (GitHubPublisher:Owner or GitHubPublisher:Username or parameter 'owner')" };
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            return new ToolResult { Success = false, Error = "Missing required repo (GitHubPublisher:Repository or parameter 'repo')" };
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            return new ToolResult { Success = false, Error = "Missing GitHubPublisher:Token (store in user-secrets / docker secrets)" };
        }

        var client = _httpClientFactory.CreateClient(nameof(PublishCustomToolsToGitHubTool));
        ConfigureGitHubClient(client, options.Token);

        var exists = await RepoExistsAsync(client, owner, repo, ct).ConfigureAwait(false);
        if (!exists)
        {
            if (!options.CreateRepoIfMissing)
            {
                return new ToolResult { Success = false, Error = $"Repository {owner}/{repo} does not exist (and CreateRepoIfMissing=false)" };
            }

            var created = await TryCreatePrivateRepoAsync(client, repo, ct).ConfigureAwait(false);
            if (!created)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"Failed to create private repo {owner}/{repo}. Ensure the token has repo permissions and the owner matches the authenticated user."
                };
            }
        }

        IReadOnlyList<CustomToolDefinition> defs;
        try
        {
            defs = await _store.GetAllAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load custom tools from store");
            return new ToolResult { Success = false, Error = "Failed to load custom tools from store" };
        }

        var normalizedRoot = NormalizePathSegment(rootPath, fallback: "tools");
        var uploaded = 0;
        var failed = 0;

        var index = defs
            .Where(d => d is not null && d.IsValid)
            .Select(d => new
            {
                id = d.Id,
                tool_name = d.ToolName,
                created_at = d.CreatedAt,
                created_by = d.CreatedByAgentName,
                requires_manual_approval = d.RequiresManualApproval,
                source_hash = d.SourceHash
            })
            .OrderBy(d => d.tool_name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var indexJson = JsonSerializer.Serialize(index, JsonIndented);
        if (await UpsertTextFileAsync(client, owner, repo, branch, CombinePath(normalizedRoot, "index.json"), indexJson,
                message: "Update custom tools index", ct).ConfigureAwait(false))
        {
            uploaded++;
        }
        else
        {
            failed++;
        }

        foreach (var def in defs)
        {
            ct.ThrowIfCancellationRequested();

            if (def is null || !def.IsValid)
            {
                continue;
            }

            var toolFolder = CombinePath(normalizedRoot, NormalizePathSegment(def.ToolName, fallback: "tool"));
            var defJson = JsonSerializer.Serialize(def, JsonIndented);

            var ok1 = await UpsertTextFileAsync(
                client,
                owner,
                repo,
                branch,
                CombinePath(toolFolder, "definition.json"),
                defJson,
                message: $"Upsert {def.ToolName} definition",
                ct).ConfigureAwait(false);

            var ok2 = await UpsertTextFileAsync(
                client,
                owner,
                repo,
                branch,
                CombinePath(toolFolder, "tool.cs"),
                def.SourceCode,
                message: $"Upsert {def.ToolName} source",
                ct).ConfigureAwait(false);

            if (ok1 && ok2)
            {
                uploaded += 2;
            }
            else
            {
                failed += (ok1 ? 0 : 1) + (ok2 ? 0 : 1);
            }
        }

        return new ToolResult
        {
            Success = failed == 0,
            Output = $"GitHub publish complete: uploaded={uploaded} failed={failed} repo={owner}/{repo} branch={branch}",
            Metadata = new Dictionary<string, object>
            {
                ["owner"] = owner,
                ["repo"] = repo,
                ["branch"] = branch,
                ["uploaded"] = uploaded,
                ["failed"] = failed,
                ["tool_count"] = defs.Count
            }
        };
    }

    private static void ConfigureGitHubClient(HttpClient client, string token)
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("InfernalHierarchy", "1.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static async Task<bool> RepoExistsAsync(HttpClient client, string owner, string repo, CancellationToken ct)
    {
        using var resp = await client.GetAsync($"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}", ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        return resp.IsSuccessStatusCode;
    }

    private static async Task<bool> TryCreatePrivateRepoAsync(HttpClient client, string repo, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            name = repo,
            @private = true,
            auto_init = true
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync("user/repos", content, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    private static async Task<bool> UpsertTextFileAsync(
        HttpClient client,
        string owner,
        string repo,
        string branch,
        string path,
        string text,
        string message,
        CancellationToken ct)
    {
        try
        {
            var sha = await TryGetFileShaAsync(client, owner, repo, branch, path, ct).ConfigureAwait(false);
            var payload = new Dictionary<string, object?>
            {
                ["message"] = message,
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty)),
                ["branch"] = branch
            };
            if (!string.IsNullOrWhiteSpace(sha))
            {
                payload["sha"] = sha;
            }

            var json = JsonSerializer.Serialize(payload, JsonIndented);
            using var body = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{path}";
            using var resp = await client.PutAsync(url, body, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryGetFileShaAsync(
        HttpClient client,
        string owner,
        string repo,
        string branch,
        string path,
        CancellationToken ct)
    {
        var url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{path}?ref={Uri.EscapeDataString(branch)}";
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("sha", out var shaEl) && shaEl.ValueKind == JsonValueKind.String)
        {
            return shaEl.GetString();
        }

        return null;
    }

    private static string NormalizePathSegment(string? value, string fallback)
    {
        var v = (value ?? string.Empty).Trim();
        v = v.Replace('\\', '/');
        while (v.StartsWith("/", StringComparison.Ordinal)) v = v[1..];
        while (v.EndsWith("/", StringComparison.Ordinal)) v = v[..^1];
        if (string.IsNullOrWhiteSpace(v))
        {
            return fallback;
        }

        var sb = new StringBuilder(v.Length);
        foreach (var ch in v)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '/' or '.') sb.Append(ch);
            else sb.Append('_');
        }

        // Prevent path traversal or escaping the intended root.
        // Keep only normal segments and drop "." and "..".
        var cleaned = sb.ToString();
        var segments = cleaned
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.Equals(s, ".", StringComparison.Ordinal) && !string.Equals(s, "..", StringComparison.Ordinal))
            .ToArray();

        var result = string.Join('/', segments);
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static string CombinePath(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a.TrimEnd('/') + "/" + b.TrimStart('/');
    }
}

