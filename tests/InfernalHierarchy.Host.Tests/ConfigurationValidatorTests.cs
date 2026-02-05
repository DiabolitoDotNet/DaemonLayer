using FluentAssertions;
using InfernalHierarchy.Agents.Orchestration;
using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class ConfigurationValidatorTests
{
    [Fact]
    public void OllamaOptionsValidator_WhenRequiredValuesMissing_Fails()
    {
        var validator = new OllamaOptionsValidator(NullLogger<OllamaOptionsValidator>.Instance);

        var result = validator.Validate(null, new OllamaOptions
        {
            BaseUrl = null!,
            DefaultModel = "",
            MaxTokens = 0,
            Temperature = 0.7
        });

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Validators_WhenValid_Succeed()
    {
        var ollama = new OllamaOptionsValidator(NullLogger<OllamaOptionsValidator>.Instance);
        var telegram = new TelegramOptionsValidator(NullLogger<TelegramOptionsValidator>.Instance);
        var memory = new MemoryOptionsValidator();
        var hierarchy = new HierarchyOptionsValidator(NullLogger<HierarchyOptionsValidator>.Instance);
        var searx = new SearXngOptionsValidator();
        var brave = new BraveSearchOptionsValidator(NullLogger<BraveSearchOptionsValidator>.Instance);

        ollama.Validate(null, new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434/v1"),
            DefaultModel = "llama3",
            MaxTokens = 1024,
            Temperature = 0.7
        }).Succeeded.Should().BeTrue();

        telegram.Validate(null, new TelegramOptions
        {
            BotToken = "",
            AllowedUserIds = []
        }).Succeeded.Should().BeTrue();

        memory.Validate(null, new MemoryOptions { DatabasePath = "data/infernal.db" }).Succeeded.Should().BeTrue();

        hierarchy.Validate(null, new HierarchyOptions
        {
            MainAgentName = "Lucifer",
            MainAgentPersonaPath = "souls/lucifer.json",
            MaxAgentDepth = 4
        }).Succeeded.Should().BeTrue();

        searx.Validate(null, new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://localhost:8080") }).Succeeded.Should().BeTrue();
        brave.Validate(null, new BraveSearchOptions { Enabled = true, ApiKey = "" }).Succeeded.Should().BeTrue();
    }
}
