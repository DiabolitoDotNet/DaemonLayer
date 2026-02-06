using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Voice;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Hosting;

/// <summary>
/// Optional startup warmup for Piper.Net TTS.
/// Keeps the interactive UI snappy by pre-loading the ONNX voice model and running a best-effort warmup.
/// </summary>
public sealed class TextToSpeechWarmupService : IHostedService
{
    private readonly TextToSpeechToolOptions _options;
    private readonly IEnumerable<TextToSpeechTool> _ttsTools;
    private readonly ILogger<TextToSpeechWarmupService> _logger;

    public TextToSpeechWarmupService(
        IOptions<TextToSpeechToolOptions> options,
        IEnumerable<ITool> tools,
        ILogger<TextToSpeechWarmupService> logger)
    {
        _options = options.Value;
        _ttsTools = tools.OfType<TextToSpeechTool>();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.UsePiperNet || !_options.PiperWarmupAtStartup)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.PiperVoicePath))
        {
            _logger.LogWarning("TTS warmup requested but TextToSpeech:PiperVoicePath is empty");
            return;
        }

        var tool = _ttsTools.FirstOrDefault();
        if (tool is null)
        {
            _logger.LogWarning("TTS warmup requested but TextToSpeechTool is not registered");
            return;
        }

        try
        {
            _logger.LogInformation("🔊 Warming up Piper.Net TTS at startup...");
            var warmed = await tool.WarmupAsync(cancellationToken).ConfigureAwait(false);
            if (warmed)
            {
                _logger.LogInformation("✅ Piper.Net TTS warmup completed");
            }
            else
            {
                _logger.LogInformation("ℹ️ Piper.Net TTS warmup skipped by configuration");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Piper.Net TTS warmup failed; continuing without warmup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
