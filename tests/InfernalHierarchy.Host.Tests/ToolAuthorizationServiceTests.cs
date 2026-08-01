using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ToolAuthorizationServiceCoverageTests
{
    [Fact]
    public void IsAuthorized_DefaultPermissions_GetAgentStatus_ShouldBeSupremeOnly()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "get_agent_status").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a2", "baal", AgentRank.Prince, "get_agent_status").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a3", "vassago", AgentRank.Duke, "get_agent_status").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a4", "worker", AgentRank.Worker, "get_agent_status").IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void IsAuthorized_WhenToolNotConfigured_ShouldDenyByDefault()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Worker, "unknown_tool");

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not configured");
    }

    [Fact]
    public void IsAuthorized_WhenCustomToolNotConfigured_ShouldBeSupremeOnly()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "custom_xml_parser").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a2", "duke", AgentRank.Duke, "custom_xml_parser").IsAuthorized.Should().BeFalse();
        sut.IsAuthorized("a3", "worker", AgentRank.Worker, "custom_xml_parser").IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void IsAuthorized_WhenToolDisabled_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:danger:Enabled"] = "false",
            ["ToolPermissions:danger:AllowedRanks"] = "Supreme,Prince,Duke,Worker",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "danger");

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("disabled");
    }

    [Fact]
    public void IsAuthorized_WhenRankNotAllowed_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:restricted:Enabled"] = "true",
            ["ToolPermissions:restricted:AllowedRanks"] = "Supreme",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "restricted").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a1", "lucifer", AgentRank.Worker, "restricted").IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void IsAuthorized_WhenBlacklistedByIdOrName_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:web_search:Enabled"] = "true",
            ["ToolPermissions:web_search:AllowedRanks"] = "Supreme,Prince,Duke,Worker",
            ["ToolPermissions:web_search:BlacklistedAgents:0"] = "a1",
            ["ToolPermissions:web_search:BlacklistedAgents:1"] = "asmodeus",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "lucifer", AgentRank.Worker, "web_search").IsAuthorized.Should().BeFalse();
        sut.IsAuthorized("a2", "asmodeus", AgentRank.Worker, "web_search").IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void IsAuthorized_WhenWhitelistPresent_ShouldRequireAgentInWhitelist()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:write_memory:Enabled"] = "true",
            ["ToolPermissions:write_memory:AllowedRanks"] = "Supreme,Prince,Duke,Worker",
            ["ToolPermissions:write_memory:WhitelistedAgents:0"] = "a1",
            ["ToolPermissions:write_memory:WhitelistedAgents:1"] = "lucifer",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "someone", AgentRank.Worker, "write_memory").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a2", "lucifer", AgentRank.Worker, "write_memory").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a2", "someone", AgentRank.Worker, "write_memory").IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void GetAuthorizedTools_ShouldReturnOnlyAuthorizedTools()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:t1:Enabled"] = "true",
            ["ToolPermissions:t1:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ToolPermissions:t2:Enabled"] = "true",
            ["ToolPermissions:t2:AllowedRanks"] = "Worker",
            ["ToolPermissions:t3:Enabled"] = "false",
            ["ToolPermissions:t3:AllowedRanks"] = "Supreme,Prince,Duke,Worker",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var toolsForDuke = sut.GetAuthorizedTools("a", "lucifer", AgentRank.Duke);
        toolsForDuke.Should().Contain("t1");
        toolsForDuke.Should().NotContain("t2");
        toolsForDuke.Should().NotContain("t3");

        var toolsForWorker = sut.GetAuthorizedTools("a", "lucifer", AgentRank.Worker);
        toolsForWorker.Should().Contain("t2");
        toolsForWorker.Should().NotContain("t1");
        toolsForWorker.Should().NotContain("t3");
    }

    [Fact]
    public void ReloadPermissions_ShouldPickUpConfigurationChanges()
    {
        var data = new Dictionary<string, string?>
        {
            ["ToolPermissions:t1:Enabled"] = "true",
            ["ToolPermissions:t1:AllowedRanks"] = "Worker",
        };

        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a", "n", AgentRank.Worker, "t1").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a", "n", AgentRank.Supreme, "t1").IsAuthorized.Should().BeFalse();

        config["ToolPermissions:t1:AllowedRanks"] = "Supreme";
        config["ToolPermissions:t2:Enabled"] = "true";
        config["ToolPermissions:t2:AllowedRanks"] = "Supreme";

        sut.ReloadPermissions();

        sut.IsAuthorized("a", "n", AgentRank.Worker, "t1").IsAuthorized.Should().BeFalse();
        sut.IsAuthorized("a", "n", AgentRank.Supreme, "t1").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a", "n", AgentRank.Supreme, "t2").IsAuthorized.Should().BeTrue();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
