namespace InfernalHierarchy.Tools.Tools.Vision;

public sealed class VisionDescribeTool : ITool
{
    private readonly VisionToolOptions _options;
    private readonly ILlmClient _llmClient;
    private readonly ILogger<VisionDescribeTool> _logger;

    public VisionDescribeTool(
        IOptions<VisionToolOptions> options,
        ILlmClient llmClient,
        ILogger<VisionDescribeTool> logger)
    {
        _options = options.Value;
        _llmClient = llmClient;
        _logger = logger;
    }

    public string Name => "vision_describe";

    public string Description => "Analyze a local image with a vision-capable model. Params: path, question (optional), prompt (optional), model (optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("Vision tool is disabled (Vision:Enabled=false)");
        }

        if (_llmClient is not IImageLlmClient imageClient)
        {
            return Fail("Configured LLM client does not support image completions");
        }

        var path = GetString(parameters, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Missing required parameter: path");
        }

        if (!File.Exists(path))
        {
            return Fail("Image file does not exist");
        }

        var fullPath = Path.GetFullPath(path);
        var root = ResolveRootDirectory(_options.RootDirectory);
        if (!IsUnderRoot(fullPath, root))
        {
            return Fail("Image file path is outside the configured RootDirectory");
        }

        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrWhiteSpace(ext) || !_options.AllowedExtensions.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail($"File extension '{ext}' is not allowed");
        }

        var fi = new FileInfo(fullPath);
        if (fi.Length > _options.MaxInputBytes)
        {
            return Fail($"Image file too large (max {_options.MaxInputBytes} bytes)");
        }

        var question = GetString(parameters, "question") ?? "Describe what is visible and answer concisely.";
        var prompt = GetString(parameters, "prompt") ?? _options.DefaultPrompt;
        var model = GetString(parameters, "model");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = string.IsNullOrWhiteSpace(_options.DefaultModel) ? null : _options.DefaultModel;
        }

        var mimeType = GetMimeType(ext);
        var imageBytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(_options.TimeoutMs));

        var response = await imageClient.GetImageCompletionAsync(
            systemPrompt: prompt,
            userMessage: question,
            imageBytes: imageBytes,
            mimeType: mimeType,
            modelOverride: model,
            ct: timeoutCts.Token).ConfigureAwait(false);

        if (response.Length > _options.MaxOutputChars)
        {
            response = response[.._options.MaxOutputChars];
        }

        _logger.LogInformation("🖼️ vision_describe completed for {Path}", fullPath);

        return new ToolResult
        {
            Success = true,
            Output = response,
            Metadata = new Dictionary<string, object>
            {
                ["path"] = fullPath,
                ["bytes"] = imageBytes.Length,
                ["mime_type"] = mimeType,
                ["model"] = model ?? string.Empty
            }
        };
    }

    private static string ResolveRootDirectory(string rootDirectory)
    {
        var root = string.IsNullOrWhiteSpace(rootDirectory) ? "data/vision" : rootDirectory;
        return Path.IsPathRooted(root) ? root : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));
    }

    private static bool IsUnderRoot(string filePath, string rootDirectory)
    {
        var root = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static ToolResult Fail(string message) => new() { Success = false, Output = string.Empty, Error = message };

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }
}