using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using LMSupply;
using LMSupply.Synthesizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.Voice;

public sealed class TextToSpeechTool : ITool, IAsyncDisposable
{
    private readonly TextToSpeechToolOptions _options;
    private readonly IProcessRunner _runner;
    private readonly ILogger<TextToSpeechTool> _logger;

    private readonly SemaphoreSlim _piperInitLock = new(1, 1);
    private ISynthesizerModel? _piperModel;

    public TextToSpeechTool(
        IOptions<TextToSpeechToolOptions> options,
        IProcessRunner runner,
        ILogger<TextToSpeechTool> logger)
    {
        _options = options.Value;
        _runner = runner;
        _logger = logger;
    }

    public string Name => "tts_speak";

    public string Description => "Synthesize speech to an audio file (local-first). Params: text.";

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

        if (_options.UsePiperNet)
        {
            if (string.IsNullOrWhiteSpace(_options.PiperVoicePath))
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
                var synthesizer = await GetOrCreatePiperSynthesizerAsync(threadCount, warmup: _options.PiperWarmupOnLoad, ct).ConfigureAwait(false);

                var synthOptions = new SynthesizeOptions
                {
                    OutputFormat = AudioFormat.Wav,
                    SpeakerId = _options.PiperSpeakerId,
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
                        ["speaker_id"] = _options.PiperSpeakerId,
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
                    Metadata = new Dictionary<string, object> { ["provider"] = "piper_net" }
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

    private async Task<ISynthesizerModel> GetOrCreatePiperSynthesizerAsync(int threadCount, CancellationToken ct)
    {
        return await GetOrCreatePiperSynthesizerAsync(threadCount, warmup: true, ct).ConfigureAwait(false);
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

        await GetOrCreatePiperSynthesizerAsync(threadCount, warmup: true, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<ISynthesizerModel> GetOrCreatePiperSynthesizerAsync(int threadCount, bool warmup, CancellationToken ct)
    {
        if (_piperModel is not null)
        {
            return _piperModel;
        }

        await _piperInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_piperModel is not null)
            {
                return _piperModel;
            }

            var modelOptions = new SynthesizerOptions
            {
                Provider = ExecutionProvider.Cpu,
                ThreadCount = threadCount == 0 ? Environment.ProcessorCount : threadCount
            };

            _logger.LogInformation("🔊 Loading Piper.Net voice model: {VoicePath}", _options.PiperVoicePath);
            _piperModel = await LocalSynthesizer.LoadAsync(_options.PiperVoicePath, modelOptions).ConfigureAwait(false);

            if (warmup)
            {
                try
                {
                    await _piperModel.WarmupAsync(ct).ConfigureAwait(false);
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

            return _piperModel;
        }
        finally
        {
            _piperInitLock.Release();
        }
    }

    private async Task DisposePiperModelAsync()
    {
        var model = _piperModel;
        _piperModel = null;
        if (model is null)
        {
            return;
        }

        try
        {
            if (model is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (model is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose Piper.Net model");
        }
    }
}
