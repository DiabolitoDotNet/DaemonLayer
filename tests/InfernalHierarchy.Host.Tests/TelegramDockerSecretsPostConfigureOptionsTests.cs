using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Telegram.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class TelegramDockerSecretsPostConfigureOptionsTests
{
    [Fact]
    public void Options_WhenEmpty_ShouldLoadFromDockerSecrets()
    {
        var root = CreateTempSecretsRoot();

        File.WriteAllText(Path.Combine(root, "telegram_bot_token"), "token-from-secret\n");
        File.WriteAllText(Path.Combine(root, "telegram_user_ids"), "123, 456\n# comment\n789\n");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DockerSecrets:RootPath"] = root
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddSingleton<IPostConfigureOptions<TelegramOptions>, TelegramDockerSecretsPostConfigureOptions>();
        services.AddOptions<TelegramOptions>().Bind(configuration.GetSection("Telegram"));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
        options.BotToken.Should().Be("token-from-secret");
        options.AllowedUserIds.Should().BeEquivalentTo(new long[] { 123, 456, 789 });
    }

    [Fact]
    public void Options_WhenConfigured_ShouldNotBeOverriddenByDockerSecrets()
    {
        var root = CreateTempSecretsRoot();

        File.WriteAllText(Path.Combine(root, "telegram_bot_token"), "token-from-secret\n");
        File.WriteAllText(Path.Combine(root, "telegram_user_ids"), "123, 456\n");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DockerSecrets:RootPath"] = root,
                ["Telegram:BotToken"] = "token-from-config",
                ["Telegram:AllowedUserIds:0"] = "999"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddSingleton<IPostConfigureOptions<TelegramOptions>, TelegramDockerSecretsPostConfigureOptions>();
        services.AddOptions<TelegramOptions>().Bind(configuration.GetSection("Telegram"));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
        options.BotToken.Should().Be("token-from-config");
        options.AllowedUserIds.Should().BeEquivalentTo(new long[] { 999 });
    }

    private static string CreateTempSecretsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
