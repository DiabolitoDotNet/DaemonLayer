namespace InfernalHierarchy.Telegram.Options;

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;

    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();
}
