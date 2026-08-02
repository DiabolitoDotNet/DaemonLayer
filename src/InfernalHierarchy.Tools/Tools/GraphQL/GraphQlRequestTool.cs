using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.GraphQL;

public sealed class GraphQlRequestTool : ITool
{
    private static readonly Regex MutationRegex = new(@"\bmutation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SubscriptionRegex = new(@"\bsubscription\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GraphQlToolOptions _options;
    private readonly GlobalExceptionHandler? _exceptionHandler;

    public GraphQlRequestTool(
        IHttpClientFactory httpClientFactory,
        IOptions<GraphQlToolOptions> options,
        GlobalExceptionHandler? exceptionHandler = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _exceptionHandler = exceptionHandler;
    }

    public string Name => "graphql_request";

    public string Description => "Execute GraphQL query requests with auth helpers and safety guardrails. Params: endpoint, query, variables (optional), operation_name (optional), headers (optional), api_key (optional), auth_scheme (optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("GraphQL tool is disabled (GraphQlTool:Enabled=false)");
        }

        var endpointRaw = GetString(parameters, "endpoint") ?? GetString(parameters, "url");
        if (string.IsNullOrWhiteSpace(endpointRaw))
        {
            return Fail("Missing required parameter: endpoint");
        }

        if (!Uri.TryCreate(endpointRaw, UriKind.Absolute, out var endpoint))
        {
            return Fail("Invalid endpoint URI");
        }

        if (!IsSchemeAllowed(endpoint))
        {
            return Fail($"URL scheme '{endpoint.Scheme}' is not allowed");
        }

        if (!IsHostAllowed(endpoint.Host))
        {
            return Fail("Host is not allowlisted");
        }

        var query = GetString(parameters, "query") ?? GetString(parameters, "document");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Fail("Missing required parameter: query");
        }

        if (_options.RequireReadOnly && (MutationRegex.IsMatch(query) || SubscriptionRegex.IsMatch(query)))
        {
            return Fail("Only read-only GraphQL operations are allowed (query only)");
        }

        if (!_options.AllowIntrospection
            && (query.Contains("__schema", StringComparison.OrdinalIgnoreCase)
                || query.Contains("__type", StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("GraphQL introspection is disabled by policy");
        }

        var payload = new Dictionary<string, object?>
        {
            ["query"] = query
        };

        var operationName = GetString(parameters, "operation_name") ?? GetString(parameters, "operationName");
        if (!string.IsNullOrWhiteSpace(operationName))
        {
            payload["operationName"] = operationName;
        }

        var variables = GetObjectDictionary(parameters, "variables");
        if (variables != null)
        {
            payload["variables"] = variables;
        }

        var client = _httpClientFactory.CreateClient(nameof(GraphQlRequestTool));
        client.Timeout = TimeSpan.FromMilliseconds(_options.TimeoutMs);

        var headers = GetStringDictionary(parameters, "headers");
        var apiKey = GetString(parameters, "api_key") ?? GetString(parameters, "apiKey");
        var authScheme = GetString(parameters, "auth_scheme") ?? "Bearer";

        var json = JsonSerializer.Serialize(payload);

        try
        {
            using var response = await ExecuteWithResilienceAsync(async token =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    if (string.Equals(authScheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    }
                    else
                    {
                        _ = req.Headers.TryAddWithoutValidation(authScheme, apiKey);
                    }
                }

                if (headers != null)
                {
                    foreach (var (key, value) in headers)
                    {
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        _ = req.Headers.TryAddWithoutValidation(key, value);
                    }
                }

                return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }, "graphql_request_tool", ct).ConfigureAwait(false);

            var bytes = await ReadUpToAsync(response, _options.MaxResponseBytes, ct).ConfigureAwait(false);
            var text = DecodeBody(bytes, response.Content.Headers);

            return new ToolResult
            {
                Success = response.IsSuccessStatusCode,
                Output = text,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                Metadata = new Dictionary<string, object>
                {
                    ["status"] = (int)response.StatusCode,
                    ["bytes"] = bytes.Length,
                    ["endpoint"] = endpoint.ToString(),
                    ["content_type"] = response.Content.Headers.ContentType?.ToString() ?? string.Empty
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fail("GraphQL request timed out");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken ct)
    {
        if (_exceptionHandler is null)
        {
            return await operation(ct).ConfigureAwait(false);
        }

        return await _exceptionHandler.ExecuteWithHandlingAsync(operation, operationName, maxRetries: 3, ct: ct).ConfigureAwait(false);
    }

    private bool IsSchemeAllowed(Uri uri)
    {
        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && _options.AllowHttpOnLocalhost)
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsHostAllowed(string host)
    {
        foreach (var allowed in _options.AllowedHosts)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            if (allowed.StartsWith(".", StringComparison.Ordinal) && host.EndsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<byte[]> ReadUpToAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (maxBytes <= 0)
        {
            return Array.Empty<byte>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var remaining = maxBytes - (int)ms.Length;
            if (remaining <= 0)
            {
                break;
            }

            var toWrite = Math.Min(read, remaining);
            await ms.WriteAsync(buffer.AsMemory(0, toWrite), ct).ConfigureAwait(false);

            if (toWrite < read)
            {
                break;
            }
        }

        return ms.ToArray();
    }

    private static string DecodeBody(byte[] bytes, HttpContentHeaders headers)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var charset = headers.ContentType?.CharSet;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
        }
        catch
        {
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static Dictionary<string, string?>? GetStringDictionary(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
        }

        if (value is Dictionary<string, string?> dict2)
        {
            return dict2;
        }

        return null;
    }

    private static Dictionary<string, object?>? GetObjectDictionary(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json.GetRawText());
            return parsed;
        }

        if (value is string s)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(s);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Error = message,
        Output = string.Empty
    };
}