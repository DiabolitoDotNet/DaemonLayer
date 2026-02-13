using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class DefaultActionParserTests
{
    [Fact]
    public void TryParse_WhenJsonHasTrailingComma_ParsesSuccessfully()
    {
        var parser = new DefaultActionParser();

        var response = "```json\n{\n  \"thought\": \"ok\",\n  \"action\": \"get_agent_status\",\n  \"actionInput\": { },\n}\n```";

        parser.TryParse(response, useJsonResponse: true, out var parsed).Should().BeTrue();
        parsed.Action.Should().Be("get_agent_status");
    }

    [Fact]
    public void TryParse_WhenJsonIsEmbeddedInText_ExtractsFirstBalancedObject()
    {
        var parser = new DefaultActionParser();

        var response = "Here you go:\n{\"thought\":\"t\",\"action\":\"email_send\",\"actionInput\":{\"to\":\"x\",\"subject\":\"s\",\"body\":\"b\"}}\n(extra stuff {not json})";

        parser.TryParse(response, useJsonResponse: true, out var parsed).Should().BeTrue();
        parsed.Action.Should().Be("email_send");
    }

    [Fact]
    public void TryParse_WhenLegacyFormatProvided_ParsesSuccessfully()
    {
        var parser = new DefaultActionParser();

        var response = "Thought: do it\nAction: send_telegram\nAction Input: {\"chat_id\":123,\"text\":\"hi\"}";

        parser.TryParse(response, useJsonResponse: false, out var parsed).Should().BeTrue();
        parsed.Action.Should().Be("send_telegram");
        parsed.ActionInputText.Should().Contain("chat_id");
    }

    [Fact]
    public void TryParse_WhenJsonActionIsFinal_NormalizesToFinalAnswer()
    {
        var parser = new DefaultActionParser();

        var response = "{\"thought\":\"t\",\"action\":\"final\",\"actionInput\":\"pong\"}";

        parser.TryParse(response, useJsonResponse: true, out var parsed).Should().BeTrue();
        parsed.Action.Should().Be("FINAL_ANSWER");
        parsed.ActionInputText.Should().Be("pong");
    }

    [Fact]
    public void TryParse_WhenLegacyActionIsFinalAnswerWords_NormalizesToFinalAnswer()
    {
        var parser = new DefaultActionParser();

        var response = "Thought: ok\nAction: Final Answer\nAction Input: done";

        parser.TryParse(response, useJsonResponse: false, out var parsed).Should().BeTrue();
        parsed.Action.Should().Be("FINAL_ANSWER");
        parsed.ActionInputText.Should().Be("done");
    }
}
