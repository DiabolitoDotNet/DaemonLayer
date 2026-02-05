using FluentAssertions;
using InfernalHierarchy.Host;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class InputValidatorAdditionalTests
{
    [Fact]
    public void SanitizeInput_NullOrEmpty_ReturnsEmpty()
    {
        InputValidator.SanitizeInput(null!).Should().BeEmpty();
        InputValidator.SanitizeInput(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeInput_ShouldTrim_RemoveNullChars_AndTruncate()
    {
        var input = "  hi\0there  ";
        InputValidator.SanitizeInput(input, maxLength: 10000).Should().Be("hithere");
    }

    [Fact]
    public void IsSafeSql_ShouldDetectCommonSqlKeywords()
    {
        InputValidator.IsSafeSql("DROP TABLE users;").Should().BeFalse();
        InputValidator.IsSafeSql("hello world").Should().BeTrue();
        InputValidator.IsSafeSql("").Should().BeTrue();
    }

    [Fact]
    public void IsSafeXss_ShouldDetectScriptTags_AndJavascriptUris()
    {
        InputValidator.IsSafeXss("<script>alert(1)</script>").Should().BeFalse();
        InputValidator.IsSafeXss("javascript:alert(1)").Should().BeFalse();
        InputValidator.IsSafeXss("plain text").Should().BeTrue();
    }

    [Fact]
    public void IsSafeCommand_ShouldDetectShellMetacharacters()
    {
        InputValidator.IsSafeCommand("echo hi").Should().BeTrue();
        InputValidator.IsSafeCommand("rm -rf /; echo pwn").Should().BeFalse();
    }

    [Fact]
    public void ValidateUserInput_ShouldReturnIssues_AndSanitizedValue()
    {
        var result = InputValidator.ValidateUserInput("  DROP TABLE x;  ");

        result.IsValid.Should().BeFalse();
        result.SanitizedValue.Should().Be("DROP TABLE x;");
        result.ValidationIssues.Should().Contain(i => i.Contains("SQL", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Lucifer", true)]
    [InlineData("Baal_01", true)]
    [InlineData("Vassago-the-Duke", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("bad*name", false)]
    public void IsValidAgentName_ShouldValidateAllowedCharacters(string name, bool expected)
    {
        InputValidator.IsValidAgentName(name).Should().Be(expected);
    }

    [Fact]
    public void IsValidFilePath_ShouldRejectTraversal_AndAbsolutePaths()
    {
        InputValidator.IsValidFilePath("../secrets.txt").Should().BeFalse();
        InputValidator.IsValidFilePath("~/.ssh/id_rsa").Should().BeFalse();
        InputValidator.IsValidFilePath("C:/Windows/system32").Should().BeFalse();

        InputValidator.IsValidFilePath("data/file.txt").Should().BeTrue();
    }

    [Fact]
    public void IsValidUrl_ShouldRespectAllowedSchemes()
    {
        InputValidator.IsValidUrl("https://example.com").Should().BeTrue();
        InputValidator.IsValidUrl("ftp://example.com").Should().BeFalse();

        InputValidator.IsValidUrl("ftp://example.com", allowedSchemes: new[] { "ftp" }).Should().BeTrue();
    }

    [Fact]
    public void SanitizeJson_Empty_ReturnsObject_And_RemovesControlChars()
    {
        InputValidator.SanitizeJson(string.Empty).Should().Be("{}");

        var json = "{\"x\":\"a\u0001b\"}";
        InputValidator.SanitizeJson(json).Should().Be("{\"x\":\"ab\"}");
    }
}
