using FluentAssertions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class LlmResponseTests
{
    [Fact]
    public void LlmResponse_ShouldAllowPropertySetters()
    {
        var response = new LlmResponse
        {
            Content = "hi",
            ModelUsed = "m",
            InputTokens = 1,
            OutputTokens = 2,
            Duration = TimeSpan.FromSeconds(1)
        };

        response.Content.Should().Be("hi");
        response.ModelUsed.Should().Be("m");
        response.InputTokens.Should().Be(1);
        response.OutputTokens.Should().Be(2);
        response.Duration.Should().Be(TimeSpan.FromSeconds(1));
    }
}
