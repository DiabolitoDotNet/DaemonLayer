using FluentAssertions;
using InfernalHierarchy.Tools.Dynamic;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class DefaultCustomToolSecurityPolicyTests
{
    private readonly DefaultCustomToolSecurityPolicy _policy = new();

    [Fact]
    public void Evaluate_WhenBlockedWordAppearsOnlyInStringLiteral_ShouldNotRequireManualApproval()
    {
        var source = """
using InfernalHierarchy.Core.Interfaces;

public sealed class CustomStringTool : ITool
{
    public string Name => "custom_string_tool";
    public string Description => "safe";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
        => Task.FromResult(new ToolResult { Success = true, Output = "File.ReadAllText should stay inside this string" });
}
""";

        var decision = _policy.Evaluate(source);

        decision.Allowed.Should().BeTrue();
        decision.RequiresManualApproval.Should().BeFalse();
        decision.MatchedRules.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenBlockedWordAppearsOnlyInComment_ShouldNotRequireManualApproval()
    {
        var source = """
using InfernalHierarchy.Core.Interfaces;

public sealed class CustomCommentTool : ITool
{
    public string Name => "custom_comment_tool";
    public string Description => "safe";

    // File.ReadAllText is mentioned here as prose only.
    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
        => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
}
""";

        var decision = _policy.Evaluate(source);

        decision.Allowed.Should().BeTrue();
        decision.RequiresManualApproval.Should().BeFalse();
        decision.MatchedRules.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenFileApiIsUsed_ShouldRequireManualApproval()
    {
        var source = """
using InfernalHierarchy.Core.Interfaces;
using System.IO;

public sealed class CustomIoTool : ITool
{
    public string Name => "custom_io_tool";
    public string Description => "io";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var content = File.ReadAllText("/tmp/file.txt");
        return Task.FromResult(new ToolResult { Success = true, Output = content });
    }
}
""";

        var decision = _policy.Evaluate(source);

        decision.Allowed.Should().BeTrue();
        decision.RequiresManualApproval.Should().BeTrue();
        decision.MatchedRules.Should().Contain(rule => rule.Contains("System.IO", StringComparison.Ordinal));
        decision.MatchedRules.Should().Contain(rule => rule.Contains("File/Directory APIs", StringComparison.Ordinal));
    }
}