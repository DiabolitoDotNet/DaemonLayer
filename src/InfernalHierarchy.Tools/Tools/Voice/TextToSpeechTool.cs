using LMSupply;
using LMSupply.Synthesizer;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Voice;

public sealed class TextToSpeechTool : ITool, IAsyncDisposable
{
    private readonly TextToSpeechToolOptions _options;
    private readonly IProcessRunner _runner;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<TextToSpeechTool> _logger;

    private readonly SemaphoreSlim _piperInitLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ISynthesizerModel> _piperModels = new(StringComparer.OrdinalIgnoreCase);

    public TextToSpeechTool(
        IOptions<TextToSpeechToolOptions> options,
        IProcessRunner runner,
        ILogger<TextToSpeechTool> logger)
        : this(options, runner, httpClientFactory: null, logger)
    {
    }

    public TextToSpeechTool(
        IOptions<TextToSpeechToolOptions> options,
        IProcessRunner runner,
        IHttpClientFactory? httpClientFactory,
        ILogger<TextToSpeechTool> logger)
    {
        _options = options.Value;
        _runner = runner;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "tts_speak";

    public string Description => "Synthesize speech to an audio file (local-first). Params: text, language (optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("TTS tool is disabled (TextToSpeech:Enabled=false)");
        }

        var text = GetString(parameters, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail("Missing required parameter: text");
        }

        if (text.Length > _options.MaxTextChars)
        {
            return Fail($"Text too long (max {_options.MaxTextChars} chars)");
        }

        var root = ResolveRootDirectory(_options.RootDirectory);
        Directory.CreateDirectory(root);

        var ext = string.IsNullOrWhiteSpace(_options.OutputExtension) ? ".wav" : _options.OutputExtension;
        if (!ext.StartsWith(".", StringComparison.Ordinal))
        {
            ext = "." + ext;
        }

        var outputPath = Path.Combine(root, $"tts_{Guid.NewGuid():N}{ext}");
        var voiceSelection = TextToSpeechLanguageRouting.Resolve(_options, parameters, text);

        if (_options.UseSidecar)
        {
            return await ExecuteWithSidecarAsync(text, voiceSelection.LanguageTag, outputPath, ct).ConfigureAwait(false);
        }

        if (_options.UsePiperNet)
        {
            if (string.IsNullOrWhiteSpace(voiceSelection.PiperVoicePath))
            {
                return Fail("TextToSpeech:PiperVoicePath is required when TextToSpeech:UsePiperNet=true");
            }

            var speed = _options.PiperSpeed;
            if (float.IsNaN(speed) || speed < 0.5f) speed = 0.5f;
            if (speed > 2.0f) speed = 2.0f;

            var threadCount = _options.PiperThreadCount;
            if (threadCount < 0) threadCount = 0;

            try
            {
                var synthesizer = await GetOrCreatePiperSynthesizerAsync(
                    voiceSelection.PiperVoicePath,
                    threadCount,
                    warmup: _options.PiperWarmupOnLoad,
                    ct).ConfigureAwait(false);

                var synthOptions = new SynthesizeOptions
                {
                    OutputFormat = AudioFormat.Wav,
                    SpeakerId = voiceSelection.SpeakerId,
                    Speed = speed
                };

                var audio = await synthesizer.SynthesizeAsync(text, synthOptions, ct).ConfigureAwait(false);
                var wavBytes = audio.ToWavBytes();

                await File.WriteAllBytesAsync(outputPath, wavBytes, ct).ConfigureAwait(false);

                if (!File.Exists(outputPath))
                {
                    return Fail("TTS completed but no output file was produced");
                }

                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length <= 0)
                {
                    return Fail("TTS produced an empty output file");
                }

                _logger.LogInformation("🔊 tts_speak (Piper.Net) produced {Path}", outputPath);

                return new ToolResult
                {
                    Success = true,
                    Output = outputPath,
                    Metadata = new Dictionary<string, object>
                    {
                        ["output_path"] = outputPath,
                        ["provider"] = "piper_net",
                        ["speaker_id"] = voiceSelection.SpeakerId,
                        ["voice_path"] = voiceSelection.PiperVoicePath,
                        ["language"] = string.IsNullOrWhiteSpace(voiceSelection.LanguageTag) ? "auto" : voiceSelection.LanguageTag,
                        ["language_auto_detected"] = voiceSelection.AutoDetectedLanguage,
                        ["speed"] = speed,
                        ["bytes"] = wavBytes.Length
                    }
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    Success = false,
                    Output = string.Empty,
                    Error = $"Piper.Net TTS failed: {ex.Message}",
                    Metadata = new Dictionary<string, object>
                    {
                        ["provider"] = "piper_net",
                        ["voice_path"] = voiceSelection.PiperVoicePath,
                        ["language"] = string.IsNullOrWhiteSpace(voiceSelection.LanguageTag) ? "auto" : voiceSelection.LanguageTag
                    }
                };
            }
        }

        var args = _options.Arguments.Select(a =>
            a.Replace("{text}", text, StringComparison.Ordinal)
             .Replace("{output}", outputPath, StringComparison.Ordinal)).ToList();

        if (args.Count == 0)
        {
            return Fail("TextToSpeech:Arguments must be configured (use {text} and {output} placeholders)");
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return Fail("TextToSpeech:ExecutablePath is required when UsePiperNet=false");
        }

        var result = await _runner.RunAsync(new ProcessRunRequest(
            FileName: _options.ExecutablePath,
            Arguments: args,
            WorkingDirectory: root,
            TimeoutMs: _options.TimeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return Fail("TTS timed out");
        }

        if (result.ExitCode != 0)
        {
            return new ToolResult
            {
                Success = false,
                Output = result.StdOut,
                Error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? $"TTS failed (exit {result.ExitCode})"
                    : $"TTS failed (exit {result.ExitCode}): {result.StdErr}",
                Metadata = new Dictionary<string, object>
                {
                    ["exit_code"] = result.ExitCode,
                    ["duration_ms"] = (long)result.Duration.TotalMilliseconds,
                    ["truncated"] = result.Truncated
                }
            };
        }

        if (!File.Exists(outputPath))
        {
            return Fail("TTS completed but no output file was produced");
        }

        _logger.LogInformation("🔊 tts_speak produced {Path}", outputPath);

        return new ToolResult
        {
            Success = true,
            Output = outputPath,
            Metadata = new Dictionary<string, object>
            {
                ["output_path"] = outputPath,
                ["language"] = string.IsNullOrWhiteSpace(voiceSelection.LanguageTag) ? "auto" : voiceSelection.LanguageTag,
                ["language_auto_detected"] = voiceSelection.AutoDetectedLanguage,
                ["duration_ms"] = (long)result.Duration.TotalMilliseconds,
                ["truncated"] = result.Truncated
            }
        };
    }

    private static string ResolveRootDirectory(string rootDirectory)
    {
        var root = string.IsNullOrWhiteSpace(rootDirectory) ? "data/voice" : rootDirectory;
        return Path.IsPathRooted(root) ? root : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));
    }

    private static ToolResult Fail(string message) => new() { Success = false, Output = string.Empty, Error = message };

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

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposePiperModelAsync().ConfigureAwait(false);
        }
        finally
        {
            _piperInitLock.Dispose();
        }
    }

    private async Task<ISynthesizerModel> GetOrCreatePiperSynthesizerAsync(string voicePath, int threadCount, CancellationToken ct)
    {
        return await GetOrCreatePiperSynthesizerAsync(voicePath, threadCount, warmup: true, ct).ConfigureAwait(false);
    }

    public async Task<bool> WarmupAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled || !_options.UsePiperNet)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.PiperVoicePath))
        {
            return false;
        }

        var threadCount = _options.PiperThreadCount;
        if (threadCount < 0) threadCount = 0;

        await GetOrCreatePiperSynthesizerAsync(_options.PiperVoicePath, threadCount, warmup: true, ct).ConfigureAwait(false);

        if (_options.EnableLanguageVoiceSelection && !string.IsNullOrWhiteSpace(_options.FrenchPiperVoicePath))
        {
            await GetOrCreatePiperSynthesizerAsync(_options.FrenchPiperVoicePath, threadCount, warmup: true, ct).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<ISynthesizerModel> GetOrCreatePiperSynthesizerAsync(string voicePath, int threadCount, bool warmup, CancellationToken ct)
    {
        if (_piperModels.TryGetValue(voicePath, out var existing))
        {
            return existing;
        }

        await _piperInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_piperModels.TryGetValue(voicePath, out existing))
            {
                return existing;
            }

            var modelOptions = new SynthesizerOptions
            {
                Provider = ExecutionProvider.Cpu,
                ThreadCount = threadCount == 0 ? Environment.ProcessorCount : threadCount
            };

            _logger.LogInformation("🔊 Loading Piper.Net voice model: {VoicePath}", voicePath);
            var model = await LocalSynthesizer.LoadAsync(voicePath, modelOptions, progress: null, cancellationToken: ct).ConfigureAwait(false);

            if (warmup)
            {
                try
                {
                    await model.WarmupAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Piper.Net warmup failed; continuing without warmup");
                }
            }

            _piperModels[voicePath] = model;
            return model;
        }
        finally
        {
            _piperInitLock.Release();
        }
    }

    private async Task DisposePiperModelsAsync()
    {
        if (_piperModels.IsEmpty)
        {
            return;
        }

        foreach (var voicePath in _piperModels.Keys.ToArray())
        {
            if (!_piperModels.TryRemove(voicePath, out var model))
            {
                continue;
            }

            try
            {
                if (model is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                if (model is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose Piper.Net model for {VoicePath}", voicePath);
            }
        }
    }

    private async Task DisposePiperModelAsync() => await DisposePiperModelsAsync().ConfigureAwait(false);

    private async Task<ToolResult> ExecuteWithSidecarAsync(string text, string? language, string outputPath, CancellationToken ct)
    {
        if (_httpClientFactory is null)
        {
            return Fail("Voice sidecar mode requires IHttpClientFactory");
        }

        var endpoint = BuildSidecarEndpoint(_options.SidecarBaseUrl, _options.SidecarSpeakPath);
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(_options.SidecarTimeoutMs > 0 ? _options.SidecarTimeoutMs : 120_000);

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["language"] = language
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Fail($"Sidecar TTS failed: {(int)response.StatusCode} {err}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            return Fail("Sidecar TTS returned empty audio payload");
        }

        await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);

        return new ToolResult
        {
            Success = true,
            Output = outputPath,
            Metadata = new Dictionary<string, object>
            {
                ["output_path"] = outputPath,
                ["provider"] = "voice_sidecar",
                ["endpoint"] = endpoint.ToString(),
                ["bytes"] = bytes.Length,
                ["language"] = string.IsNullOrWhiteSpace(language) ? "auto" : language
            }
        };
    }

    private static Uri BuildSidecarEndpoint(Uri baseUrl, string path)
    {
        var trimmed = string.IsNullOrWhiteSpace(path) ? "/speak" : path.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri(baseUrl, trimmed.StartsWith('/') ? trimmed : "/" + trimmed);
    }
}
