using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Tools.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ToolRateLimitingOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenDisabled_ReturnsSuccess()
    {
        var validator = new ToolRateLimitingOptionsValidator();
        var result = validator.Validate(null, new ToolRateLimitingOptions { Enabled = false });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenEnabled_AndRuleInvalid_ReturnsFailure()
    {
        var validator = new ToolRateLimitingOptionsValidator();
        var options = new ToolRateLimitingOptions
        {
            Enabled = true,
            DefaultRule = new FixedWindowRateLimitRule { PermitLimit = 0, WindowSeconds = 0 }
        };

        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
    }
}
