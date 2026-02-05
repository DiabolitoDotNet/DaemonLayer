using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using InfernalHierarchy.Telegram.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class SecretRotationServiceTests
{
    private sealed class TestableSecretRotationService : SecretRotationService
    {
        public TestableSecretRotationService(
            TestOptionsMonitor<TelegramOptions> telegramOptions,
            TestOptionsMonitor<OllamaOptions> ollamaOptions,
            TestOptionsMonitor<BraveSearchOptions> braveOptions,
            TelegramBotClientFactory botFactory)
            : base(
                NullLogger<SecretRotationService>.Instance,
                telegramOptions,
                ollamaOptions,
                braveOptions,
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                botFactory)
        {
        }

        public Task RunAsync(CancellationToken token) => ExecuteAsync(token);
    }

    [Fact]
    public static void TelegramBotClientFactory_CachesAndRecreatesClient()
    {
        var factory = new TelegramBotClientFactory(NullLogger<TelegramBotClientFactory>.Instance);

        var c1 = factory.GetOrCreateClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345");
        var c2 = factory.GetOrCreateClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345");

        ReferenceEquals(c1, c2).Should().BeTrue();

        factory.RecreateClient("987654321:QRSTUVWXYZABCDEFGHIJKLMNopqrstuv67890");
        factory.Client.Should().NotBeNull();
        ReferenceEquals(c1, factory.Client).Should().BeFalse();
    }

    [Fact]
    public static async Task SecretRotationService_WhenTelegramTokenChanges_RecreatesClientAsync()
    {
        var telegramMonitor = new TestOptionsMonitor<TelegramOptions>(new TelegramOptions
        {
            BotToken = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345",
            AllowedUserIds = []
        });

        var ollamaMonitor = new TestOptionsMonitor<OllamaOptions>(new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434"),
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        });

        var braveMonitor = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions
        {
            Enabled = false,
            ApiKey = ""
        });

        var factory = new TelegramBotClientFactory(NullLogger<TelegramBotClientFactory>.Instance);
        var initialClient = factory.GetOrCreateClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345");

        using var cts = new CancellationTokenSource();
        using var service = new TestableSecretRotationService(telegramMonitor, ollamaMonitor, braveMonitor, factory);
        var runTask = service.RunAsync(cts.Token);

        // Ensure callbacks are registered before triggering changes.
        await WaitUntilAsync(() => telegramMonitor.ListenerCount == 1, TimeSpan.FromSeconds(2));

        telegramMonitor.Set(new TelegramOptions { BotToken = "987654321:QRSTUVWXYZABCDEFGHIJKLMNopqrstuv67890", AllowedUserIds = [] });

        await WaitUntilAsync(() => factory.Client is not null && !ReferenceEquals(initialClient, factory.Client), TimeSpan.FromSeconds(2));

        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public static async Task SecretRotationService_WhenTelegramTokenBecomesEmpty_DoesNotRecreateClientAsync()
    {
        var telegramMonitor = new TestOptionsMonitor<TelegramOptions>(new TelegramOptions
        {
            BotToken = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345",
            AllowedUserIds = []
        });

        var ollamaMonitor = new TestOptionsMonitor<OllamaOptions>(new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434"),
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        });

        var braveMonitor = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions
        {
            Enabled = false,
            ApiKey = "initial"
        });

        var factory = new TelegramBotClientFactory(NullLogger<TelegramBotClientFactory>.Instance);
        var initialClient = factory.GetOrCreateClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345");

        using var cts = new CancellationTokenSource();
        using var service = new TestableSecretRotationService(telegramMonitor, ollamaMonitor, braveMonitor, factory);
        var runTask = service.RunAsync(cts.Token);

        await WaitUntilAsync(() => telegramMonitor.ListenerCount == 1, TimeSpan.FromSeconds(2));

        telegramMonitor.Set(new TelegramOptions { BotToken = "", AllowedUserIds = [] });

        // Give callbacks a moment; should not recreate.
        await Task.Delay(50);
        ReferenceEquals(initialClient, factory.Client).Should().BeTrue();

        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public static async Task SecretRotationService_WhenOllamaUrlChanges_UpdatesLastUrlAsync()
    {
        var telegramMonitor = new TestOptionsMonitor<TelegramOptions>(new TelegramOptions
        {
            BotToken = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345",
            AllowedUserIds = []
        });

        var ollamaMonitor = new TestOptionsMonitor<OllamaOptions>(new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434"),
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        });

        var braveMonitor = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions
        {
            Enabled = false,
            ApiKey = "initial"
        });

        var factory = new TelegramBotClientFactory(NullLogger<TelegramBotClientFactory>.Instance);

        using var cts = new CancellationTokenSource();
        using var service = new TestableSecretRotationService(telegramMonitor, ollamaMonitor, braveMonitor, factory);
        var runTask = service.RunAsync(cts.Token);

        await WaitUntilAsync(() => ollamaMonitor.ListenerCount == 1, TimeSpan.FromSeconds(2));

        var newUrl = new Uri("http://localhost:11435");
        ollamaMonitor.Set(new OllamaOptions
        {
            BaseUrl = newUrl,
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        });

        await WaitUntilAsync(() => GetLastOllamaUrl(service) == newUrl, TimeSpan.FromSeconds(2));

        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public static async Task SecretRotationService_WhenBraveApiKeyChanges_UpdatesLastKeyAsync()
    {
        var telegramMonitor = new TestOptionsMonitor<TelegramOptions>(new TelegramOptions
        {
            BotToken = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi12345",
            AllowedUserIds = []
        });

        var ollamaMonitor = new TestOptionsMonitor<OllamaOptions>(new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434"),
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        });

        var braveMonitor = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions
        {
            Enabled = true,
            ApiKey = "initial"
        });

        var factory = new TelegramBotClientFactory(NullLogger<TelegramBotClientFactory>.Instance);

        using var cts = new CancellationTokenSource();
        using var service = new TestableSecretRotationService(telegramMonitor, ollamaMonitor, braveMonitor, factory);
        var runTask = service.RunAsync(cts.Token);

        await WaitUntilAsync(() => braveMonitor.ListenerCount == 1, TimeSpan.FromSeconds(2));

        braveMonitor.Set(new BraveSearchOptions
        {
            Enabled = true,
            ApiKey = "new-key"
        });

        await WaitUntilAsync(() => GetLastBraveApiKey(service) == "new-key", TimeSpan.FromSeconds(2));

        await cts.CancelAsync();
        await runTask;
    }

    private static Uri? GetLastOllamaUrl(SecretRotationService service)
    {
        var field = typeof(SecretRotationService)
            .GetField("_lastOllamaUrl", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (Uri?)field!.GetValue(service);
    }

    private static string? GetLastBraveApiKey(SecretRotationService service)
    {
        var field = typeof(SecretRotationService)
            .GetField("_lastBraveApiKey", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (string?)field!.GetValue(service);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition not met within timeout.");
            }

            await Task.Delay(10);
        }
    }
}
