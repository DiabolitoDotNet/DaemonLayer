using System.Threading.Channels;

namespace InfernalHierarchy.Host.Voice;

public interface IVoiceCopilotTtsQueue
{
    bool TryEnqueue(string sessionId, string text);
}

public sealed class VoiceCopilotTtsQueue : BackgroundService, IVoiceCopilotTtsQueue
{
    private readonly Channel<TtsJob> _channel;
    private readonly IToolRegistry _tools;
    private readonly ILogger<VoiceCopilotTtsQueue> _logger;

    private sealed record TtsJob(string SessionId, string Text);

    public VoiceCopilotTtsQueue(IToolRegistry tools, ILogger<VoiceCopilotTtsQueue> logger)
    {
        _tools = tools;
        _logger = logger;

        _channel = Channel.CreateBounded<TtsJob>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public bool TryEnqueue(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return _channel.Writer.TryWrite(new TtsJob(sessionId, text));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await _tools.ExecuteToolWithTrackingAsync(
                    toolName: "tts_speak",
                    parameters: new Dictionary<string, object>
                    {
                        ["text"] = job.Text,
                        ["session_id"] = job.SessionId
                    },
                    agentId: "voice_copilot",
                    agentRank: "interface",
                    agentName: "voice_copilot",
                    ct: stoppingToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    _logger.LogWarning("VoiceCopilot TTS failed (session {SessionId}): {Error}", job.SessionId, result.Error);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VoiceCopilot TTS job failed (session {SessionId})", job.SessionId);
            }
        }
    }
}
