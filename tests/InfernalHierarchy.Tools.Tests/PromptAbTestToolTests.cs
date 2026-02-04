using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class PromptAbTestToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithVariantsJson_ReturnsWinnerAndReportAsync()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, string user, CancellationToken _) =>
            {
                // Variant A returns valid JSON; Variant B returns plain text.
                return system.Contains("VariantA", StringComparison.OrdinalIgnoreCase)
                    ? "{\"ok\":true}"
                    : "not json";
            });

        var personas = new Mock<IPersonaLoader>();
        var logger = Mock.Of<ILogger<PromptAbTestTool>>();

        var tool = new PromptAbTestTool(llm.Object, personas.Object, logger);

        var variantsJson = "[" +
                           "{\"name\":\"A\",\"system_prompt\":\"You are VariantA\"}," +
                           "{\"name\":\"B\",\"system_prompt\":\"You are VariantB\"}" +
                           "]";

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Return a result",
            ["trials"] = 3,
            ["must_be_json"] = true,
            ["variants_json"] = variantsJson
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!["winner"].Should().Be("A");

        using var doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("winner").GetProperty("name").GetString().Should().Be("A");
        doc.RootElement.GetProperty("results").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithVariantsAsJsonElement_ParsesArrayAsync()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hello world");

        var personas = new Mock<IPersonaLoader>();
        var logger = Mock.Of<ILogger<PromptAbTestTool>>();
        var tool = new PromptAbTestTool(llm.Object, personas.Object, logger);

        using var variantsDoc = JsonDocument.Parse("[{\"name\":\"A\",\"system_prompt\":\"p1\"},{\"name\":\"B\",\"system_prompt\":\"p2\"}]");

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants"] = variantsDoc.RootElement,
            ["trials"] = 1
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeTrue();
        using var reportDoc = JsonDocument.Parse(result.Output);
        reportDoc.RootElement.GetProperty("results").GetArrayLength().Should().Be(2);
    }
}
