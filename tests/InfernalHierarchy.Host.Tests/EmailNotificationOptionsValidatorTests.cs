using FluentAssertions;
using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class EmailNotificationOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenDisabled_ReturnsSuccess()
    {
        var validator = new EmailNotificationOptionsValidator();

        var result = validator.Validate(name: null, new EmailNotificationOptions { Enabled = false });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabledAndMissingFields_ReturnsFailure()
    {
        var validator = new EmailNotificationOptionsValidator();

        var result = validator.Validate(name: null, new EmailNotificationOptions { Enabled = true });

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Email:Host");
        result.FailureMessage.Should().Contain("Email:Username");
        result.FailureMessage.Should().Contain("Email:Password");
        result.FailureMessage.Should().Contain("Email:FromAddress");
    }
}
