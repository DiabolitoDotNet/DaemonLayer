using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests.Entities;

public class PersonaTests
{
    [Fact]
    public void Persona_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var persona = new Persona
        {
            Name = "Lucifer",
            DemonTitle = "The Lightbringer"
        };

        // Assert
        persona.Name.Should().Be("Lucifer");
        persona.DemonTitle.Should().Be("The Lightbringer");
        persona.Specializations.Should().BeEmpty();
        persona.AvailableTools.Should().BeEmpty();
        persona.Personality.Should().NotBeNull();
    }

    [Fact]
    public void Persona_ShouldSupportToolConfiguration()
    {
        // Arrange & Act
        var persona = new Persona
        {
            Name = "Vassago",
            AvailableTools = new[] { "web_search", "read_memory", "write_memory" }
        };

        // Assert
        persona.AvailableTools.Should().HaveCount(3);
        persona.AvailableTools.Should().Contain("web_search");
    }

    [Fact]
    public void PersonalityTraits_ShouldHaveDefaults()
    {
        // Arrange & Act
        var traits = new PersonalityTraits();

        // Assert
        traits.Tone.Should().Be("Professional");
        traits.Approach.Should().Be("Analytical");
        traits.Verbosity.Should().Be(5);
        traits.UseDemonicTheme.Should().BeTrue();
    }

    [Fact]
    public void Persona_ShouldSupportCustomInstructions()
    {
        // Arrange
        var persona = new Persona { Name = "TestDemon" };

        // Act
        persona.CustomInstructions["greeting"] = "🔥 Test greeting";
        persona.CustomInstructions["errorHandling"] = "Handle with care";

        // Assert
        persona.CustomInstructions.Should().ContainKey("greeting");
        persona.CustomInstructions["greeting"].Should().Contain("Test greeting");
    }
}
