namespace InfernalHierarchy.Telegram.Services;

public interface ITelegramInboundSimulator
{
    Task SimulateInboundTextAsync(long chatId, long userId, string messageText, CancellationToken ct = default);
}
