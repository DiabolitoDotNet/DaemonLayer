namespace InfernalHierarchy.Telegram.Options;

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;

    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Optional preamble injected into every Telegram → Lucifer message.
    /// Use this for language preference and user profile context.
    /// Prefer sourcing this from docker secrets rather than committing it.
    /// </summary>
    public string LuciferPreamble { get; set; } = string.Empty;

    /// <summary>
    /// Optional chat id to notify when the Telegram bot successfully starts.
    /// If not set (0), and exactly one AllowedUserIds is configured, the service will
    /// attempt to notify that user id (works for private chats where chat id == user id).
    /// </summary>
    public long StartupNotificationChatId { get; set; } = 0;
}
