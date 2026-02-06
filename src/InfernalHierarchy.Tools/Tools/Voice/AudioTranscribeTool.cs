using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.Voice;

public sealed class AudioTranscribeTool : ITool
{
    private readonly VoiceTranscriptionToolOptions _options;
    private readonly IProcessRunner _runner;
    private readonly ILogger<AudioTranscribeTool> _logger;

    public AudioTranscribeTool(
        IOptions<VoiceTranscriptionToolOptions> options,
        IProcessRunner runner,
        ILogger<AudioTranscribeTool> logger)
    {
        _options = options.Value;
        _runner = runner;
        _logger = logger;
    }

    public string Name => "audio_transcribe";

    public string Description => "Transcribe local audio to text (local-first). Params: path.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("Audio transcription tool is disabled (VoiceTranscription:Enabled=false)");
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return Fail("VoiceTranscription:ExecutablePath is required when enabled");
        }

        var path = GetString(parameters, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Missing required parameter: path");
        }

        if (!File.Exists(path))
        {
            return Fail("Audio file does not exist");
        }

        var fullPath = Path.GetFullPath(path);
        var root = ResolveRootDirectory(_options.RootDirectory);

        if (!IsUnderRoot(fullPath, root))
        {
            return Fail("Audio file path is outside the configured RootDirectory");
        }

        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrWhiteSpace(ext) || !_options.AllowedExtensions.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail($"File extension '{ext}' is not allowed");
        }

        var fi = new FileInfo(fullPath);
        if (fi.Length > _options.MaxInputBytes)
        {
            return Fail($"Audio file too large (max {_options.MaxInputBytes} bytes)");
        }

        Directory.CreateDirectory(root);

        var args = _options.Arguments.Select(a => a.Replace("{input}", fullPath, StringComparison.Ordinal)).ToList();
        if (args.Count == 0)
        {
            // Reasonable default for whisper.cpp-like CLIs.
            args.Add(fullPath);
        }

        var result = await _runner.RunAsync(new ProcessRunRequest(
            FileName: _options.ExecutablePath,
            Arguments: args,
            WorkingDirectory: root,
            TimeoutMs: _options.TimeoutMs,
            MaxOutputBytes: _options.MaxOutputBytes), ct).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return Fail("Transcription timed out");
        }

        if (result.ExitCode != 0)
        {
            return new ToolResult
            {
                Success = false,
                Output = result.StdOut,
                Error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? $"Transcription failed (exit {result.ExitCode})"
                    : $"Transcription failed (exit {result.ExitCode}): {result.StdErr}",
                Metadata = new Dictionary<string, object>
                {
                    ["exit_code"] = result.ExitCode,
                    ["duration_ms"] = (long)result.Duration.TotalMilliseconds,
                    ["truncated"] = result.Truncated
                }
            };
        }

        var transcript = (result.StdOut ?? string.Empty).Trim();
        _logger.LogInformation("🎙️ audio_transcribe completed ({Ms}ms)", (long)result.Duration.TotalMilliseconds);

        return new ToolResult
        {
            Success = true,
            Output = transcript,
            Metadata = new Dictionary<string, object>
            {
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

    private static bool IsUnderRoot(string filePath, string rootDirectory)
    {
        var root = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
}
