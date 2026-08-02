using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Personas.Loading;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Personas.Tests;

public sealed class JsonSkillPackCatalogTests : IDisposable
{
    private readonly string _skillsDirectory;
    private readonly JsonSkillPackCatalog _catalog;

    public JsonSkillPackCatalogTests()
    {
        _skillsDirectory = Path.Combine(Path.GetTempPath(), $"test_skills_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_skillsDirectory);

        _catalog = new JsonSkillPackCatalog(Mock.Of<ILogger<JsonSkillPackCatalog>>(), _skillsDirectory);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenFileMissing()
    {
        var pack = await _catalog.GetByIdAsync("missing");
        pack.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldLoadSkillPack_WhenFileExists()
    {
        var data = new
        {
            id = "code-review",
            name = "Code Review",
            enabled = true,
            additionalTools = new[] { "read_memory" }
        };

        await File.WriteAllTextAsync(
            Path.Combine(_skillsDirectory, "code-review.json"),
            JsonSerializer.Serialize(data));

        var pack = await _catalog.GetByIdAsync("code-review");

        pack.Should().NotBeNull();
        pack!.Id.Should().Be("code-review");
        pack.AdditionalTools.Should().Contain("read_memory");
    }

    [Fact]
    public async Task GetAllAsync_ShouldOnlyReturnEnabledPacks_OrderedByPriority()
    {
        var lowPriority = new
        {
            id = "a-pack",
            name = "A",
            enabled = true,
            priority = 20
        };

        var highPriority = new
        {
            id = "b-pack",
            name = "B",
            enabled = true,
            priority = 5
        };

        var disabled = new
        {
            id = "c-pack",
            name = "C",
            enabled = false,
            priority = 1
        };

        await File.WriteAllTextAsync(Path.Combine(_skillsDirectory, "a-pack.json"), JsonSerializer.Serialize(lowPriority));
        await File.WriteAllTextAsync(Path.Combine(_skillsDirectory, "b-pack.json"), JsonSerializer.Serialize(highPriority));
        await File.WriteAllTextAsync(Path.Combine(_skillsDirectory, "c-pack.json"), JsonSerializer.Serialize(disabled));

        var packs = await _catalog.GetAllAsync();

        packs.Select(p => p.Id).Should().Equal("b-pack", "a-pack");
    }

    public void Dispose()
    {
        if (Directory.Exists(_skillsDirectory))
        {
            Directory.Delete(_skillsDirectory, recursive: true);
        }
    }
}
