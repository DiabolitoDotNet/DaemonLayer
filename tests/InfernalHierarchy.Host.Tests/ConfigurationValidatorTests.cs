using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Host;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class ConfigurationValidatorTests
{
    [Fact]
    public void StartAsync_WhenRequiredValuesMissing_ThrowsInvalidOperationException()
    {
        var validator = new ConfigurationValidator(
            Options.Create(new OllamaOptions
            {
                BaseUrl = null!,
                DefaultModel = "",
                MaxTokens = 0,
                Temperature = 0.7
            }),
            Options.Create(new TelegramOptions
            {
                BotToken = "",
                AllowedUserIds = []
            }),
            Options.Create(new MemoryOptions { DatabasePath = "" }),
            Options.Create(new HierarchyOptions
            {
                MainAgentName = "",
                MainAgentPersonaPath = "",
                MaxAgentDepth = 0
            }),
            Options.Create(new SearXNGOptions { Enabled = true, BaseUrl = null! }),
            Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "" }),
            NullLogger<ConfigurationValidator>.Instance);

        Action act = () => validator.StartAsync(CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Configuration validation failed*");
    }

    [Fact]
    public void StartAsync_WhenConfigurationIsValid_CreatesMemoryDirectoryAndDoesNotThrow()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "InfernalHierarchy.Host.Tests", Guid.NewGuid().ToString("N"));
        var memoryPath = Path.Combine(tempRoot, "memory", "infernal.db");

        Directory.Exists(Path.GetDirectoryName(memoryPath)!).Should().BeFalse();

        var personaFile = Path.Combine(tempRoot, "persona.json");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(personaFile, "{}");

        var validator = new ConfigurationValidator(
            Options.Create(new OllamaOptions
            {
                BaseUrl = new Uri("http://localhost:11434"),
                DefaultModel = "llama3",
                MaxTokens = 1024,
                Temperature = 0.7
            }),
            Options.Create(new TelegramOptions
            {
                BotToken = "",
                AllowedUserIds = []
            }),
            Options.Create(new MemoryOptions { DatabasePath = memoryPath }),
            Options.Create(new HierarchyOptions
            {
                MainAgentName = "Lucifer",
                MainAgentPersonaPath = personaFile,
                MaxAgentDepth = 4
            }),
            Options.Create(new SearXNGOptions { Enabled = false }),
            Options.Create(new BraveSearchOptions { Enabled = false, ApiKey = "" }),
            NullLogger<ConfigurationValidator>.Instance);

        try
        {
            validator.StartAsync(CancellationToken.None);

            Directory.Exists(Path.GetDirectoryName(memoryPath)!).Should().BeTrue();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
