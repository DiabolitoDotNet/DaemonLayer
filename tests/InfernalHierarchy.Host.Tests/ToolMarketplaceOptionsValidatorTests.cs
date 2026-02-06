using FluentAssertions;
using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Tools.Marketplace;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ToolMarketplaceOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenDisabled_Succeeds()
    {
        var validator = new ToolMarketplaceOptionsValidator();
        var result = validator.Validate(null, new ToolMarketplaceOptions { Enabled = false });
        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WhenEnabled_RequiresDirectoryAndAllowlist()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var result = validator.Validate(null, new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = ""
        });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabled_WithRelativeDirectory_InvalidChars_Fails()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var result = validator.Validate(null, new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "bad\0path"
        });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabled_WithAbsoluteDirectory_MustExist()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var result = validator.Validate(null, options);
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabled_WithEmptyAllowlist_Fails()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "plugins"
        };

        var result = validator.Validate(null, options);
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabled_WithInvalidLimits_Fails()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "plugins",
            MaxPluginBytes = 0,
            RescanIntervalSeconds = 0
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var result = validator.Validate(null, options);
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnabled_WithValidRelativeConfig_Succeeds()
    {
        var validator = new ToolMarketplaceOptionsValidator();

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "plugins",
            MaxPluginBytes = 123,
            RescanIntervalSeconds = 5
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var result = validator.Validate(null, options);
        result.Should().Be(ValidateOptionsResult.Success);
    }
}
