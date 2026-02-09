using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Host.Voice;

public sealed class VoiceCopilotResult
{
    public required string SessionId { get; init; }
    public required string ReplyText { get; init; }
    public required string SpeechText { get; init; }
}

public sealed class VoiceCopilotService
{
    private sealed class SessionState
    {
        public readonly object Gate = new();
        public DateTime LastSeenUtc;
        public List<(string Role, string Content)> Messages = new();
    }

    private static readonly Regex CodeFenceRegex = new("```[\\s\\S]*?```", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new("\\[([^\\]]+)\\]\\(([^)]+)\\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownBulletsRegex = new("^(\\s*[-*+]\\s+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownHeadingRegex = new("^(\\s*#{1,6}\\s+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MultiWhitespaceRegex = new("\\s{2,}", RegexOptions.Compiled);
    private static readonly Regex ReasoningLikeRegex = new(
        "(\\bje dois\\b|\\bl'utilisateur\\b|\\bc'est clair\\b|\\bil veut\\b|\\bcontraintes?\\b|\\bréponse\\s+concise\\b|\\bje vais\\b|\\bje vais\\b|\\bje vais\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IOptions<VoiceCopilotOptions> _options;
    private readonly ILlmClient _llm;
    private readonly IStreamingLlmClient? _streamingLlm;
    private readonly ITunableLlmClient? _tunableLlm;
    private readonly ILogger<VoiceCopilotService> _logger;

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public VoiceCopilotService(
        IOptions<VoiceCopilotOptions> options,
        ILlmClient llm,
        ILogger<VoiceCopilotService> logger)
    {
        _options = options;
        _llm = llm;
        _streamingLlm = llm as IStreamingLlmClient;
        _tunableLlm = llm as ITunableLlmClient;
        _logger = logger;
    }

    public async Task<VoiceCopilotResult> GetReplyAsync(
        string transcript,
        string? sessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new ArgumentException("Transcript is required", nameof(transcript));
        }

        MaybePruneSessions();

        var options = _options.Value;
        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"voice-{Guid.NewGuid():N}"
            : sessionId.Trim();

        var session = _sessions.GetOrAdd(effectiveSessionId, _ => new SessionState { LastSeenUtc = DateTime.UtcNow });

        List<(string Role, string Content)> snapshot;
        lock (session.Gate)
        {
            session.LastSeenUtc = DateTime.UtcNow;
            snapshot = session.Messages.ToList();
        }

        var systemPrompt = BuildSystemPrompt(options, snapshot);
        var userMessage = transcript.Trim();

        var reply = await GenerateReplyAsync(systemPrompt, userMessage, options, ct).ConfigureAwait(false);
        var replyText = PostProcessReply(reply, options);
        var speechText = SanitizeForSpeech(replyText, options);

        lock (session.Gate)
        {
            session.LastSeenUtc = DateTime.UtcNow;
            session.Messages.Add(("user", userMessage));
            session.Messages.Add(("assistant", replyText));

            var max = Math.Max(0, options.MaxHistoryMessages);
            if (max > 0 && session.Messages.Count > max)
            {
                session.Messages = session.Messages.Skip(session.Messages.Count - max).ToList();
            }
        }

        return new VoiceCopilotResult
        {
            SessionId = effectiveSessionId,
            ReplyText = replyText,
            SpeechText = speechText
        };
    }

    private async Task<string> GenerateReplyAsync(
        string systemPrompt,
        string userMessage,
        VoiceCopilotOptions options,
        CancellationToken ct)
    {
        if (_streamingLlm is null)
        {
            if (_tunableLlm is null)
            {
                return await _llm.GetCompletionAsync(systemPrompt, userMessage, ct).ConfigureAwait(false);
            }

            return await _tunableLlm.GetCompletionWithOptionsAsync(
                systemPrompt,
                userMessage,
                temperature: options.Temperature,
                maxTokens: options.MaxTokens,
                ct: ct).ConfigureAwait(false);
        }

        var sb = new StringBuilder(capacity: 256);

        IAsyncEnumerable<string> stream = _tunableLlm is null
            ? _streamingLlm.GetStreamingCompletionAsync(systemPrompt, userMessage, ct)
            : _tunableLlm.GetStreamingCompletionWithOptionsAsync(systemPrompt, userMessage, options.Temperature, options.MaxTokens, ct);

        await foreach (var chunk in stream.WithCancellation(ct))
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                sb.Append(chunk);

                if (options.MaxReplyChars > 0 && sb.Length >= options.MaxReplyChars * 2)
                {
                    break;
                }
            }
        }

        var streamed = sb.ToString();
        if (!string.IsNullOrWhiteSpace(streamed))
        {
            return streamed;
        }

        // Some models/servers can stream only non-user-visible fields (e.g., reasoning) or otherwise produce
        // no content chunks. Fall back to a non-streaming completion so we still return a usable reply.
        _logger.LogWarning("Streaming completion yielded no content; falling back to non-streaming completion.");
        if (_tunableLlm is null)
        {
            return await _llm.GetCompletionAsync(systemPrompt, userMessage, ct).ConfigureAwait(false);
        }

        return await _tunableLlm.GetCompletionWithOptionsAsync(
            systemPrompt,
            userMessage,
            temperature: options.Temperature,
            maxTokens: options.MaxTokens,
            ct: ct).ConfigureAwait(false);
    }

    private static string BuildSystemPrompt(VoiceCopilotOptions options, List<(string Role, string Content)> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine(options.SystemPrompt);
        sb.AppendLine("Contraintes: réponse courte (1-2 phrases), sans Markdown, terminer par une question, et rester dans le concret.");
        sb.AppendLine($"Budget: max {options.MaxTokens} tokens de sortie (approx).");

        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Contexte récent (ne pas recopier, juste utiliser):");
            foreach (var (role, content) in history)
            {
                var label = role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "A" : "U";
                sb.Append(label).Append(": ").AppendLine(TrimForPrompt(content, 500));
            }
        }

        return sb.ToString();
    }

    private static string TrimForPrompt(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        if (t.Length <= maxChars) return t;
        return t.Substring(0, maxChars) + "…";
    }

    private static string PostProcessReply(string raw, VoiceCopilotOptions options)
    {
        var t = raw ?? string.Empty;
        t = t.Trim();

        if (string.IsNullOrWhiteSpace(t))
        {
            return "Peux-tu préciser ?";
        }

        // Avoid returning model "thinking" / meta commentary (common with reasoning models).
        // We prefer a safe, user-facing, short question over leaking chain-of-thought.
        if (LooksLikeReasoningOrMeta(t))
        {
            return "Bonjour ! Comment puis-je t’aider ?";
        }

        // Keep it short.
        if (options.MaxReplyChars > 0 && t.Length > options.MaxReplyChars)
        {
            t = TruncateAtWordBoundary(t, options.MaxReplyChars);
        }

        t = t.Trim('"', '\'', ' ', '\n', '\r', '\t');

        if (string.IsNullOrWhiteSpace(t))
        {
            return "Peux-tu préciser ?";
        }

        // Ensure it ends with a question.
        if (!t.EndsWith("?", StringComparison.Ordinal))
        {
            t = t.TrimEnd('.', '!', '…') + " ?";
        }

        return t;
    }

    private static bool LooksLikeReasoningOrMeta(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains("\n", StringComparison.Ordinal)) return true;
        return ReasoningLikeRegex.IsMatch(value);
    }

    private static string TruncateAtWordBoundary(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0 || value.Length <= maxChars) return value;

        var slice = value.Substring(0, maxChars);
        var lastSpace = slice.LastIndexOf(' ');
        if (lastSpace <= Math.Max(10, maxChars / 4))
        {
            return slice.Trim();
        }

        return slice.Substring(0, lastSpace).Trim();
    }

    private static string SanitizeForSpeech(string text, VoiceCopilotOptions options)
    {
        var t = text ?? string.Empty;

        t = CodeFenceRegex.Replace(t, string.Empty);
        t = MarkdownLinkRegex.Replace(t, "$1");
        t = InlineCodeRegex.Replace(t, "$1");
        t = MarkdownHeadingRegex.Replace(t, string.Empty);
        t = MarkdownBulletsRegex.Replace(t, string.Empty);

        t = t.Replace("**", string.Empty).Replace("__", string.Empty).Replace("*", string.Empty).Replace("_", string.Empty);
        t = MultiWhitespaceRegex.Replace(t, " ");
        t = t.Trim();

        if (options.MaxReplyChars > 0 && t.Length > options.MaxReplyChars)
        {
            t = t.Substring(0, options.MaxReplyChars).Trim();
        }

        return t;
    }

    private void MaybePruneSessions()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPruneUtc) < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastPruneUtc = now;
        var ttl = _options.Value.SessionTtl;

        foreach (var kvp in _sessions)
        {
            if ((now - kvp.Value.LastSeenUtc) > ttl)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }
}
