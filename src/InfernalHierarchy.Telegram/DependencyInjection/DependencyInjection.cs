using Microsoft.Extensions.DependencyInjection;

namespace InfernalHierarchy.Telegram.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfernalTelegramCommandHandlers(this IServiceCollection services)
    {
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.StartCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.HelpCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.StatusCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.SummonCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.KillCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.MemoryCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.UsageCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.LearningCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.ModelsCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.SuspendCommandHandler>();
        services.AddSingleton<ITelegramCommandHandler, DefaultCommandHandlers.ResumeCommandHandler>();
        return services;
    }
}
