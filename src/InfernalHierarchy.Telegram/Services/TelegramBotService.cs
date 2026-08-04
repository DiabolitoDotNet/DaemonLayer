using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using CoreMessageType = InfernalHierarchy.Core.Entities.MessageType;
using System.Text.RegularExpressions;
using IOFile = System.IO.File;

namespace InfernalHierarchy.Telegram.Services;

/// <summary>
/// Telegram Bot service for receiving commands and sending responses
/// </summary>
public class TelegramBotService : BackgroundService
    , ITelegramInboundSimulator
{
    private const string TelegramAgentId = "telegram";
    private static readonly HashSet<string> PresencePingMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Canonical (lowercase, no accents, no punctuation, collapsed spaces)
        "are you there",
        "still there",
        "you there",
        "tu es la",
        "t'es la",
        "toujours la",
        "encore la",
        "tu es encore la",
        "t'es encore la",
        "es tu la",
        "allo",
        "ping",
        "test"
    };

    private readonly ILogger<TelegramBotService> _logger;
    private readonly IMessageBus _messageBus;
    private readonly TelegramOptions _options;
    private readonly TelegramVoiceOptions _voiceOptions;
    private readonly IToolRegistry? _toolRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, ITelegramCommandHandler> _commandHandlers;
    private ITelegramBotClient? _botClient;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        IOptions<TelegramVoiceOptions> voiceOptions,
        IMessageBus messageBus,
        IToolRegistry? toolRegistry,
        ILogger<TelegramBotService> logger,
        IServiceProvider serviceProvider,
        IEnumerable<ITelegramCommandHandler>? commandHandlers = null)
    {
        _options = options.Value;
        _voiceOptions = voiceOptions.Value;
        _messageBus = messageBus;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _serviceProvider = serviceProvider;

        var handlers = (commandHandlers?.ToList() ?? DefaultCommandHandlers.CreateAll()).ToList();
        _commandHandlers = handlers
            .GroupBy(h => h.Command, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogWarning("⚠️ Telegram bot token not configured. Telegram service disabled.");
            return;
        }

        _botClient = new TelegramBotClient(_options.BotToken);

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("🤖 Telegram bot started: @{Username}", me.Username);

            await TryNotifyStartupAsync(_botClient, me.Username, stoppingToken).ConfigureAwait(false);

            // Start a background listener that forwards agent reports back to Telegram.
            // Agents reply to the sender via message bus (ToAgentId = task.FromAgentId).
            // We publish tasks with FromAgentId = "telegram", so listening as "telegram" lets us
            // receive the final Report (and forward it to the originating chat).
            var forwarderTask = Task.Run(() => ForwardAgentMessagesToTelegramAsync(stoppingToken), stoppingToken);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
            };

            await _botClient.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);

            // ReceiveAsync completes on cancellation/error; ensure forwarder is also awaited.
            await forwarderTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💀 Telegram bot failed to start");
        }
    }

    private async Task ForwardAgentMessagesToTelegramAsync(CancellationToken ct)
    {
        if (_botClient is null)
        {
            return;
        }

        try
        {
            await foreach (var message in _messageBus.SubscribeAsync(TelegramAgentId, ct))
            {
                if (message.Type is not (CoreMessageType.Report or CoreMessageType.Notification or CoreMessageType.ToolResult))
                {
                    continue;
                }

                if (!TryGetTelegramChatId(message, out var chatId))
                {
                    _logger.LogDebug(
                        "Skipping agent message {MessageId} from {From}: missing telegram_chat_id payload",
                        message.Id,
                        message.FromAgentId);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message.Content))
                {
                    _logger.LogDebug(
                        "Skipping agent message {MessageId} from {From}: empty content (Type={Type})",
                        message.Id,
                        message.FromAgentId,
                        message.Type);
                    continue;
                }

                var text = FormatForTelegram(message);

                try
                {
                    var chunks = SplitTelegramMessage(text);
                    var sentAny = false;

                    foreach (var chunk in chunks)
                    {
                        await _botClient.SendMessage(chatId, chunk, cancellationToken: ct);

                        if (!sentAny && _voiceOptions is { Enabled: true, ReplyWithVoice: true })
                        {
                            // Voice replies should stay short; use only the first chunk.
                            await TrySendVoiceReplyAsync(_botClient, chatId, chunk, ct).ConfigureAwait(false);
                        }

                        sentAny = true;
                    }

                    if (sentAny)
                    {
                        var correlationId = ResolveCorrelationId(message);
                        _logger.LogInformation(
                            "📨 Forwarded agent message {MessageId} from {From} to Telegram chat {ChatId} | CorrelationId: {CorrelationId}",
                            message.Id,
                            message.FromAgentId,
                            chatId,
                            correlationId);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to forward agent message {MessageId} from {From} to Telegram chat {ChatId}",
                        message.Id,
                        message.FromAgentId,
                        chatId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram forwarder loop failed");
        }
    }

    private async Task TryNotifyStartupAsync(ITelegramBotClient botClient, string? botUsername, CancellationToken ct)
    {
        try
        {
            var chatId = _options.StartupNotificationChatId;
            if (chatId == 0 && _options.AllowedUserIds.Length == 1)
            {
                chatId = _options.AllowedUserIds[0];
            }

            if (chatId == 0)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(botUsername) ? "(unknown)" : "@" + botUsername;
            await botClient.SendMessage(chatId, $"✅ InfernalHierarchy is up. Telegram bot online: {name}", cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telegram startup notification failed (best-effort)");
        }
    }

    private static string FormatForTelegram(AgentMessage message)
    {
        var text = message.Content ?? string.Empty;

        // Special-case: common success path for email tool.
        // Example: "Task E2E-EMAIL-3 completed: Email successfully sent to ... Telegram→Lucifer→SMTP workflow is operational."
        if (text.Contains("Email successfully sent", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Email sent", StringComparison.OrdinalIgnoreCase))
        {
            var email = TryExtractEmail(text);
            var masked = MaskEmail(email);
            var summary = string.IsNullOrWhiteSpace(email)
                ? "✅ Email sent"
                : $"✅ Email sent to {masked}";

            return summary;
        }

        // Generic: strip noisy prefix "Task X completed:" if present.
        var idx = text.IndexOf(" completed:", StringComparison.OrdinalIgnoreCase);
        if (text.StartsWith("Task ", StringComparison.OrdinalIgnoreCase) && idx > 0)
        {
            var after = text[(idx + " completed:".Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(after))
            {
                text = after;
            }
        }

        // For user-facing reports, do not shorten the content.
        // Long reports are handled by message chunking at send-time.
        if (message.Type is not CoreMessageType.Report)
        {
            // Generic: keep it short (first sentence) for readability.
            var firstSentenceEnd = text.IndexOf('.', StringComparison.Ordinal);
            if (firstSentenceEnd > 0 && firstSentenceEnd < 240)
            {
                text = text[..(firstSentenceEnd + 1)];
            }
        }

        return text.Trim();
    }

    private static string? TryExtractEmail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Simple, pragmatic email extractor.
        var m = Regex.Match(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        return m.Success ? m.Value : null;
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return email;

        var local = email[..at];
        var domain = email[(at + 1)..];

        var visible = Math.Min(2, local.Length);
        var maskedLocal = visible == local.Length
            ? local
            : local[..visible] + new string('*', Math.Max(3, local.Length - visible));

        return $"{maskedLocal}@{domain}";
    }

    private static bool TryGetTelegramChatId(AgentMessage message, out long chatId)
    {
        chatId = default;

        if (!message.Payload.TryGetValue("telegram_chat_id", out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            chatId = raw switch
            {
                long l => l,
                int i => i,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToInt64(raw)
            };

            return chatId != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTelegramCorrelationId(long chatId, long userId)
    {
        return $"tg:{chatId}:{userId}:{Guid.NewGuid():N}";
    }

    private static string ResolveCorrelationId(AgentMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            return message.CorrelationId;
        }

        if (message.Payload.TryGetValue("telegram_correlation_id", out var payloadCorrelation)
            && payloadCorrelation is not null
            && !string.IsNullOrWhiteSpace(payloadCorrelation.ToString()))
        {
            return payloadCorrelation.ToString()!;
        }

        return message.Id;
    }

    private static string BuildLuciferContent(string userText, string? preamble)
    {
        var p = preamble?.Trim();
        if (string.IsNullOrWhiteSpace(p))
        {
            return userText;
        }

        // Keep the user's message clearly delineated for the agent.
        return $"{p}\n\n---\nDemande utilisateur (Telegram):\n{userText}";
    }

    private static bool IsPresencePing(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var canonical = CanonicalizePresencePing(messageText);
        return PresencePingMessages.Contains(canonical);
    }

    private static string CanonicalizePresencePing(string messageText)
    {
        var normalized = messageText.Trim();

        // Normalize apostrophes/hyphens first so we can collapse whitespace consistently.
        normalized = normalized.Replace('’', '\'');
        normalized = normalized.Replace('–', '-');
        normalized = normalized.Replace('—', '-');
        normalized = normalized.Replace('-', ' ');

        // Drop accents (e.g., "là" -> "la", "allô" -> "allo").
        normalized = normalized.Normalize(NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        normalized = sb
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();

        // Remove common trailing punctuation and any stray punctuation characters.
        normalized = Regex.Replace(normalized, @"[\?\!\.,;:]+", " ");

        // Collapse whitespace.
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.Trim();

        return normalized;
    }

    internal async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message || message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        await ProcessIncomingTextAsync(
            botClient,
            chatId,
            userId,
            messageText,
            message.MessageId,
            ct).ConfigureAwait(false);
    }

    public async Task SimulateInboundTextAsync(long chatId, long userId, string messageText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return;
        }

        var botClient = _botClient;
        if (botClient is null)
        {
            if (string.IsNullOrWhiteSpace(_options.BotToken))
            {
                throw new InvalidOperationException("Telegram bot token is not configured.");
            }

            botClient = new TelegramBotClient(_options.BotToken);
        }

        await ProcessIncomingTextAsync(
            botClient,
            chatId,
            userId,
            messageText,
            messageId: null,
            ct).ConfigureAwait(false);
    }

    private async Task ProcessIncomingTextAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        string messageText,
        int? messageId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return;
        }

        if (_options.AllowedUserIds.Length > 0 && !_options.AllowedUserIds.Contains(userId))
        {
            _logger.LogWarning("🚫 Unauthorized user {UserId} attempted to send: {Text}", userId, messageText);
            await botClient.SendMessage(chatId, "❌ You are not authorized to use this bot.", cancellationToken: ct);
            return;
        }

        var correlationId = CreateTelegramCorrelationId(chatId, userId);
        _logger.LogInformation("📩 Telegram message from {UserId}: {Text} | CorrelationId: {CorrelationId}", userId, messageText, correlationId);

        if (!messageText.StartsWith('/') && IsPresencePing(messageText))
        {
            _logger.LogInformation("⚡ Fast-path presence ping for chat {ChatId}", chatId);
            await botClient.SendMessage(chatId, "Oui. Je suis là.", cancellationToken: ct);
            return;
        }

        try
        {
            if (messageText.StartsWith('/'))
            {
                await HandleCommandAsync(botClient, chatId, messageText, correlationId, ct);
            }
            else
            {
                // Lightweight receipt acknowledgement (avoid noisy "Task queued..." spam).
                // Prefer a reaction (👀) on the user's message, not a new message.
                if (messageId is not null)
                {
                    await TryAcknowledgeReceiptAsync(botClient, chatId, messageId.Value, ct).ConfigureAwait(false);
                }

                var agentMessage = new AgentMessage
                {
                    FromAgentId = TelegramAgentId,
                    ToAgentId = "lucifer",
                    Type = CoreMessageType.Task,
                    Content = BuildLuciferContent(messageText, _options.LuciferPreamble),
                    CorrelationId = correlationId,
                    Payload = new Dictionary<string, object>
                    {
                        ["telegram_chat_id"] = chatId,
                        ["telegram_user_id"] = userId,
                        ["telegram_correlation_id"] = correlationId
                    }
                };

                await _messageBus.PublishAsync(agentMessage, ct);
            }
        }
        catch (Exception ex)
        {
            var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();
            var userMessage = "❌ An error occurred processing your request.";

            if (exceptionHandler != null)
            {
                var handlingResult = await exceptionHandler.HandleExceptionAsync(ex, $"TelegramMessage_{userId}", ct: ct);

                userMessage = handlingResult.ShouldRetry
                    ? $"⚠️ {handlingResult.Message} (will retry automatically)"
                    : $"❌ {handlingResult.Message}";

                _logger.LogError(
                    ex,
                    "🔥 Failed to handle Telegram message | Category: {Category} | CorrelationId: {CorrelationId} | TelegramCorrelationId: {TelegramCorrelationId}",
                    handlingResult.Category,
                    handlingResult.CorrelationId,
                    correlationId);
            }
            else
            {
                _logger.LogError(ex, "Failed to handle Telegram message | CorrelationId: {CorrelationId}", correlationId);
                userMessage = $"❌ Error: {ex.Message}";
            }

            await botClient.SendMessage(chatId, userMessage, cancellationToken: ct);
        }
    }

    private async Task HandleCommandAsync(ITelegramBotClient botClient, long chatId, string command, string correlationId, CancellationToken ct)
    {
        try
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var cmd = parts[0];

            if (!_commandHandlers.TryGetValue(cmd, out var handler))
            {
                await botClient.SendMessage(chatId,
                    "❓ Unknown command. Use /help for available commands.",
                    cancellationToken: ct);
                return;
            }

            var context = new TelegramCommandContext(
                BotClient: botClient,
                ChatId: chatId,
                RawText: command,
                Parts: parts,
                MessageBus: _messageBus,
                Logger: _logger,
                CorrelationId: correlationId);

            await handler.HandleAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command: {Command}", command);
            await botClient.SendMessage(chatId,
                $"❌ Error executing command: {ex.Message}",
                cancellationToken: ct);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        var exceptionHandler = _serviceProvider.GetService<GlobalExceptionHandler>();

        if (exceptionHandler != null)
        {
            var handlingResult = exceptionHandler.HandleExceptionAsync(
                exception,
                "TelegramPolling",
                ct: ct).GetAwaiter().GetResult();

            _logger.LogError(
                exception,
                "🔥 Telegram polling error | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                handlingResult.Category,
                handlingResult.ShouldRetry,
                handlingResult.CorrelationId);
        }
        else
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiException => $"Telegram API Error:\n[{apiException.ErrorCode}]\n{apiException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(exception, "Telegram polling error: {ErrorMessage}", errorMessage);
        }

        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (_botClient == null)
        {
            _logger.LogWarning("Cannot send message: Telegram bot not initialized");
            return;
        }

        try
        {
            var chunks = SplitTelegramMessage(text);
            foreach (var chunk in chunks)
            {
                await _botClient.SendMessage(chatId, chunk, cancellationToken: ct);
            }

            _logger.LogDebug("📤 Sent Telegram message(s) to {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram message to {ChatId}", chatId);
        }
    }

    private const int TelegramMaxMessageLength = 3800;

    private static IEnumerable<string> SplitTelegramMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return string.Empty;
            yield break;
        }

        var remaining = text;
        while (remaining.Length > TelegramMaxMessageLength)
        {
            var window = remaining.AsSpan(0, TelegramMaxMessageLength);

            // Prefer splitting on a newline or space close to the end of the window.
            var splitAt = FindSplitIndex(window);
            if (splitAt <= 0)
            {
                splitAt = TelegramMaxMessageLength;
            }

            yield return remaining[..splitAt].TrimEnd();
            remaining = remaining[splitAt..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static int FindSplitIndex(ReadOnlySpan<char> window)
    {
        // Scan backwards but only within a limited range to avoid O(n^2)
        // behavior on huge messages.
        var start = Math.Max(0, window.Length - 400);
        for (var i = window.Length - 1; i >= start; i--)
        {
            if (window[i] == '\n')
            {
                return i + 1;
            }

            if (char.IsWhiteSpace(window[i]))
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static async Task TryAcknowledgeReceiptAsync(ITelegramBotClient botClient, long chatId, int messageId, CancellationToken ct)
    {
        try
        {
            // Telegram reaction API (setMessageReaction)
            await botClient.SetMessageReaction(
                chatId,
                messageId,
                reaction: new[] { new ReactionTypeEmoji { Emoji = "👀" } },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort acknowledgement; ignore.
        }
    }

    private async Task TrySendVoiceReplyAsync(ITelegramBotClient botClient, long chatId, string text, CancellationToken ct)
    {
        if (_toolRegistry is null)
        {
            _logger.LogDebug("Telegram voice reply requested but no IToolRegistry is available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ToolResult tts;
        try
        {
            tts = await _toolRegistry.ExecuteToolWithTrackingAsync(
                _voiceOptions.SpeakToolName,
                new Dictionary<string, object> { ["text"] = text },
                agentId: TelegramAgentId,
                agentRank: "interface",
                agentName: "Telegram",
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram voice reply: TTS tool invocation failed");
            return;
        }

        if (!tts.Success || string.IsNullOrWhiteSpace(tts.Output) || !IOFile.Exists(tts.Output))
        {
            _logger.LogDebug("Telegram voice reply: TTS did not produce an output file. Success={Success} Error={Error}", tts.Success, tts.Error);
            return;
        }

        var wavPath = tts.Output;
        var oggPath = Path.ChangeExtension(wavPath, ".ogg");

        try
        {
            // Prefer sending as a voice note (ogg/opus).
            var converted = await TryConvertToOggOpusAsync(wavPath, oggPath, ct).ConfigureAwait(false);
            var sendPath = converted ? oggPath : wavPath;

            var fileInfo = new FileInfo(sendPath);
            if (fileInfo.Length <= 0 || fileInfo.Length > _voiceOptions.MaxAudioBytes)
            {
                _logger.LogDebug(
                    "Telegram voice reply: audio file size out of bounds. Path={Path} Bytes={Bytes} Max={Max}",
                    sendPath,
                    fileInfo.Length,
                    _voiceOptions.MaxAudioBytes);
                return;
            }

            await using var stream = IOFile.OpenRead(sendPath);

            if (converted)
            {
                await botClient.SendVoice(
                    chatId,
                    InputFile.FromStream(stream, Path.GetFileName(sendPath)),
                    cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                // Fallback: send as a regular audio file if conversion failed.
                await botClient.SendAudio(
                    chatId,
                    InputFile.FromStream(stream, Path.GetFileName(sendPath)),
                    cancellationToken: ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram voice reply: failed to send voice/audio");
        }
        finally
        {
            TryDeleteQuietly(oggPath);
            // Leave the original wav in place (owned by TTS tool root directory); pruning handled elsewhere.
        }
    }

    private static async Task<bool> TryConvertToOggOpusAsync(string inputWavPath, string outputOggPath, CancellationToken ct)
    {
        try
        {
            if (IOFile.Exists(outputOggPath))
            {
                IOFile.Delete(outputOggPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(inputWavPath);
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("libopus");
            psi.ArgumentList.Add("-b:a");
            psi.ArgumentList.Add("32k");
            psi.ArgumentList.Add(outputOggPath);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            using var reg = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore.
                }
            });

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return process.ExitCode == 0 && IOFile.Exists(outputOggPath);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (IOFile.Exists(path))
            {
                IOFile.Delete(path);
            }
        }
        catch
        {
            // Ignore.
        }
    }
}
