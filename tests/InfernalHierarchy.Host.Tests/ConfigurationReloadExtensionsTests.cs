using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class ConfigurationReloadExtensionsTests
{
    [Fact]
    public void ForceReload_WhenConfigurationRoot_CallsReload()
    {
        var root = new Mock<IConfigurationRoot>(MockBehavior.Strict);
        root.Setup(r => r.Reload());

        root.Object.ForceReload();

        root.Verify(r => r.Reload(), Times.Once);
    }

    [Fact]
    public void HasChanged_WhenValuesUnchanged_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Section:A"] = "1",
                ["Section:B"] = "2"
            })
            .Build();

        var previous = new Dictionary<string, string?>
        {
            ["Section:A"] = "1",
            ["Section:B"] = "2"
        };

        config.HasChanged("Section", previous).Should().BeFalse();
    }

    [Fact]
    public void HasChanged_WhenValueChanges_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Section:A"] = "1",
            })
            .Build();

        var previous = new Dictionary<string, string?>
        {
            ["Section:A"] = "1",
        };

        config.HasChanged("Section", previous).Should().BeFalse();

        config["Section:A"] = "2";
        config.HasChanged("Section", previous).Should().BeTrue();
    }
}
