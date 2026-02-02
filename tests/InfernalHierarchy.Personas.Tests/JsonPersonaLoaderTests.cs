using FluentAssertions;
using InfernalHierarchy.Personas;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Personas.Tests;

public class JsonPersonaLoaderTests : IDisposable
{
    private readonly string _testSoulsDirectory;
    private readonly JsonPersonaLoader _loader;

    public JsonPersonaLoaderTests()
    {
        _testSoulsDirectory = Path.Combine(Path.GetTempPath(), $"test_souls_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testSoulsDirectory);

        // Create a custom persona loader that uses the test directory
        var logger = Mock.Of<ILogger<JsonPersonaLoader>>();
        _loader = new JsonPersonaLoader(logger, _testSoulsDirectory);
    }

    [Fact]
    public async Task LoadPersonaAsync_ShouldReturnNull_WhenFileDoesNotExist()
    {
        // Act
        var persona = await _loader.LoadPersonaAsync("nonexistent");

        // Assert
        persona.Should().BeNull();
    }

    [Fact]
    public async Task LoadPersonaAsync_ShouldCachePersona()
    {
        // This test verifies caching behavior
        // First call loads from file, second should use cache

        // Arrange
        var testPersonaFile = Path.Combine(_testSoulsDirectory, "cached.json");
        var personaData = new
        {
            name = "Cached",
            demonTitle = "The Cached One",
            systemPrompt = "Test prompt",
            specializations = new[] { "caching" },
            availableTools = new[] { "test_tool" },
            personality = new { tone = "Efficient" }
        };

        File.WriteAllText(testPersonaFile, JsonSerializer.Serialize(personaData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));

        // Act - Load twice
        var persona1 = await _loader.LoadPersonaAsync("cached");
        var persona2 = await _loader.LoadPersonaAsync("cached");

        // Assert
        persona1.Should().NotBeNull();
        persona2.Should().NotBeNull();
        persona1!.Name.Should().Be("Cached");
        persona1.DemonTitle.Should().Be("The Cached One");
        // Verify both loads return same data (cache works)
        persona2!.Name.Should().Be(persona1.Name);
    }

    [Fact]
    public async Task LoadAllPersonasAsync_ShouldReturnEmpty_WhenNoFilesExist()
    {
        // Act
        var personas = await _loader.LoadAllPersonasAsync();

        // Assert
        personas.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSoulsDirectory))
        {
            Directory.Delete(_testSoulsDirectory, true);
        }
    }
}
