using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class PromptAbTestToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTaskMissing_ReturnsErrorAsync()
    {
        var llm = new Mock<ILlmClient>();
        var personas = new Mock<IPersonaLoader>();
        var logger = Mock.Of<ILogger<PromptAbTestTool>>();

        var tool = new PromptAbTestTool(llm.Object, personas.Object, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("task");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLessThanTwoVariants_ReturnsErrorAsync()
    {
        var llm = new Mock<ILlmClient>();
        var personas = new Mock<IPersonaLoader>();
        var logger = Mock.Of<ILogger<PromptAbTestTool>>();

        var tool = new PromptAbTestTool(llm.Object, personas.Object, logger);

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants_json"] = "[{\"name\":\"A\",\"system_prompt\":\"p1\"}]"
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("At least 2 variants");
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidVariantsJson_ReturnsInvalidVariantsErrorAsync()
    {
        var llm = new Mock<ILlmClient>();
        var personas = new Mock<IPersonaLoader>();
        var logger = Mock.Of<ILogger<PromptAbTestTool>>();

        var tool = new PromptAbTestTool(llm.Object, personas.Object, logger);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants_json"] = "not-json"
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("Invalid variants");
    }

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

    [Fact]
    public async Task ExecuteAsync_WithPersonaVariant_AppliesPrependAppend_AndClampsTrialsAsync()
    {
        var capturedSystemPrompts = new List<string>();

        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, string _, CancellationToken _) =>
            {
                capturedSystemPrompts.Add(system);
                return "ok";
            });

        var personas = new Mock<IPersonaLoader>();
        personas.Setup(x => x.LoadPersonaAsync("vassago", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona { Name = "vassago", SystemPrompt = "BASE_PROMPT" });

        var tool = new PromptAbTestTool(llm.Object, personas.Object, Mock.Of<ILogger<PromptAbTestTool>>());

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Do something",
            // Force clamping: TryGetInt parses 0, then clamp to 1.
            ["trials"] = 0,
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"persona\":\"vassago\",\"prepend\":\"PRE\",\"append\":\"POST\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!["trials"].Should().Be(1);

        capturedSystemPrompts.Should().ContainSingle(s => s.Contains("BASE_PROMPT", StringComparison.Ordinal));
        capturedSystemPrompts.Should().ContainSingle(s => s.Contains("PRE", StringComparison.Ordinal));
        capturedSystemPrompts.Should().ContainSingle(s => s.Contains("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedContainsCommaSeparated_ChoosesBestVariantAsync()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, string _, CancellationToken _) =>
                system.Contains("P1", StringComparison.Ordinal) ? "alpha beta" : "alpha");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["expected_contains"] = "alpha, beta",
            ["trials"] = 1,
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata!["winner"].Should().Be("A");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLlmThrows_ReturnsSuccessWithErrorsInReportAsync()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var parameters = new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["trials"] = 2,
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        };

        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeTrue();

        using var reportDoc = JsonDocument.Parse(result.Output);
        var results = reportDoc.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(2);

        var first = results.EnumerateArray().First();
        first.GetProperty("errors").GetArrayLength().Should().Be(2);
        first.GetProperty("scores").EnumerateArray().Select(x => x.GetDouble()).All(x => x == 0).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidExpectedRegex_ShouldNotThrow_AndShouldScoreAsUnmet()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("anything");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["trials"] = 1,
            ["expected_regex"] = "[[",
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        using var report = JsonDocument.Parse(result.Output);
        report.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("averageScore").GetDouble())
            .All(s => s == 0)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoCriteriaAndResponseNonEmpty_ShouldUseBaselineScore()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, string _, CancellationToken _) =>
                system.Contains("P1", StringComparison.Ordinal) ? "hi" : "hello");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["trials"] = 1,
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata!["winner"].Should().Be("A");

        using var report = JsonDocument.Parse(result.Output);
        var avg = report.RootElement.GetProperty("results")[0].GetProperty("averageScore").GetDouble();
        avg.Should().Be(0.25);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVariantResponseWhitespace_ShouldScoreZero()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, string _, CancellationToken _) =>
                system.Contains("P1", StringComparison.Ordinal) ? "  " : "ok");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["trials"] = 1,
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata!["winner"].Should().Be("B");
    }

    [Fact]
    public async Task ExecuteAsync_WithVariantsAsEnumerableObjects_ShouldParse()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var variants = new object[]
        {
            new Dictionary<string, object> { ["name"] = "A", ["systemPrompt"] = "P1" },
            new Dictionary<string, object> { ["name"] = "B", ["systemPrompt"] = "P2" }
        };

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants"] = variants,
            ["trials"] = 1
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        using var reportDoc = JsonDocument.Parse(result.Output);
        reportDoc.RootElement.GetProperty("results").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithVariantsAsStringParameter_ShouldParse()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants"] = "[{\"name\":\"A\",\"systemPrompt\":\"P1\"},{\"name\":\"B\",\"systemPrompt\":\"P2\"}]",
            ["trials"] = 1
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithPersonaVariantNotFound_ShouldReturnInvalidVariantsError()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var personas = new Mock<IPersonaLoader>();
        personas.Setup(x => x.LoadPersonaAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        var tool = new PromptAbTestTool(llm.Object, personas.Object, Mock.Of<ILogger<PromptAbTestTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"persona\":\"missing\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]",
            ["trials"] = 1
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("Invalid variants");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldPropagateOperationCanceledException()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string __, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return "ok";
            });

        var tool = new PromptAbTestTool(llm.Object, Mock.Of<IPersonaLoader>(), Mock.Of<ILogger<PromptAbTestTool>>());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(25));

        var act = async () => await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Do something",
            ["variants_json"] = "[" +
                             "{\"name\":\"A\",\"system_prompt\":\"P1\"}," +
                             "{\"name\":\"B\",\"system_prompt\":\"P2\"}" +
                             "]",
            ["trials"] = 3
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
