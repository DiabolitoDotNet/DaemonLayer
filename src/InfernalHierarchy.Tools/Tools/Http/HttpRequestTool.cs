using System.Net.Http.Headers;
using System.Text;

namespace InfernalHierarchy.Tools.Tools.Http;

public sealed class HttpRequestTool : ITool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpRequestToolOptions _options;
    private readonly ILogger<HttpRequestTool> _logger;
    private readonly GlobalExceptionHandler? _exceptionHandler;

    public HttpRequestTool(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpRequestToolOptions> options,
        ILogger<HttpRequestTool> logger,
        GlobalExceptionHandler? exceptionHandler = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _exceptionHandler = exceptionHandler;
    }

    public string Name => "http_request";

    public string Description => "Perform an HTTP request (allowlisted). Params: url, method (optional), headers (optional), body (optional), content_type (optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("HTTP request tool is disabled (HttpTool:Enabled=false)");
        }

        var urlRaw = GetString(parameters, "url");
        if (string.IsNullOrWhiteSpace(urlRaw))
        {
            return Fail("Missing required parameter: url");
        }

        if (!Uri.TryCreate(urlRaw, UriKind.Absolute, out var uri))
        {
            return Fail("Invalid url");
        }

        if (!IsSchemeAllowed(uri))
        {
            return Fail($"URL scheme '{uri.Scheme}' is not allowed");
        }

        if (!IsHostAllowed(uri.Host))
        {
            return Fail("Host is not allowlisted");
        }

        var method = (GetString(parameters, "method") ?? "GET").Trim().ToUpperInvariant();
        if (!_options.AllowedMethods.Any(m => string.Equals(m.Trim(), method, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail($"HTTP method '{method}' is not allowed");
        }

        var headers = GetDictionary(parameters, "headers");
        var body = GetString(parameters, "body");
        var contentType = GetString(parameters, "content_type") ?? "application/json";

        var client = _httpClientFactory.CreateClient(nameof(HttpRequestTool));
        client.Timeout = TimeSpan.FromMilliseconds(_options.TimeoutMs);

        try
        {
            using var response = await ExecuteWithResilienceAsync(
                async token =>
                {
                    using var request = CreateRequest(method, uri, headers, body, contentType);
                    var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode && IsTransientStatus(resp.StatusCode))
                    {
                        throw new HttpRequestException(
                            $"Transient HTTP failure {(int)resp.StatusCode} ({resp.StatusCode})",
                            inner: null,
                            statusCode: resp.StatusCode);
                    }

                    return resp;
                },
                operationName: "http_request_tool",
                ct).ConfigureAwait(false);

            var status = (int)response.StatusCode;

            var bytes = await ReadUpToAsync(response, _options.MaxResponseBytes, ct).ConfigureAwait(false);
            var text = DecodeBody(bytes, response.Content.Headers);

            _logger.LogInformation("🌐 http_request {Method} {Url} -> {Status} ({Bytes} bytes)", method, uri, status, bytes.Length);

            return new ToolResult
            {
                Success = response.IsSuccessStatusCode,
                Output = text,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {status}",
                Metadata = new Dictionary<string, object>
                {
                    ["status"] = status,
                    ["content_type"] = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                    ["bytes"] = bytes.Length
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fail("HTTP request timed out");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        if (_exceptionHandler is null)
        {
            return await operation(ct).ConfigureAwait(false);
        }

        return await _exceptionHandler
            .ExecuteWithHandlingAsync(operation, operationName, maxRetries: 3, ct: ct)
            .ConfigureAwait(false);
    }

    private static bool IsTransientStatus(System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        return code >= 500 || code == 429 || code == 408;
    }

    private static HttpRequestMessage CreateRequest(
        string method,
        Uri uri,
        Dictionary<string, string?>? headers,
        string? body,
        string contentType)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);

        if (headers != null)
        {
            foreach (var (k, v) in headers)
            {
                if (string.IsNullOrWhiteSpace(k) || v is null)
                {
                    continue;
                }

                if (k.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _ = request.Headers.TryAddWithoutValidation(k, v);
            }
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        return request;
    }

    private bool IsSchemeAllowed(Uri uri)
    {
        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && _options.AllowHttpOnLocalhost)
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
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

            // ".example.com" means any subdomain of example.com
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
            // ignore and fall back
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Output = string.Empty,
        Error = message
    };

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString()
        };
    }

    private static Dictionary<string, string?>? GetDictionary(Dictionary<string, object> parameters, string key)
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
}
