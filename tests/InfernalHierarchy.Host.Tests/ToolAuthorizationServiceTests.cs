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
    public void IsAuthorized_WhenCreateCustomToolExplicitlyDelegated_ShouldAllowCreationButNotInvocationByDefault()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:create_custom_tool:Enabled"] = "true",
            ["ToolPermissions:create_custom_tool:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ToolPermissions:create_custom_tool:WhitelistedAgents:0"] = "vassago",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "create_custom_tool").IsAuthorized.Should().BeTrue();

        var invokeDecision = sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "custom_http_tool");
        invokeDecision.IsAuthorized.Should().BeFalse();
        invokeDecision.Reason.Should().Contain("Supreme-only");
    }

    [Fact]
    public void IsAuthorized_WhenCustomToolInvocationIsExplicitlyDelegated_ShouldAllowConfiguredAgent()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:create_custom_tool:Enabled"] = "true",
            ["ToolPermissions:create_custom_tool:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ToolPermissions:create_custom_tool:WhitelistedAgents:0"] = "vassago",
            ["ToolPermissions:custom_http_tool:Enabled"] = "true",
            ["ToolPermissions:custom_http_tool:AllowedRanks"] = "Duke",
            ["ToolPermissions:custom_http_tool:WhitelistedAgents:0"] = "vassago",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "create_custom_tool").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "custom_http_tool").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a2", "other", AgentRank.Duke, "custom_http_tool").IsAuthorized.Should().BeFalse();
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

    [Fact]
    public void IsAuthorized_WhenExecutionProfileDeniesTool_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:http_request:Enabled"] = "true",
            ["ToolPermissions:http_request:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:DefaultProfile"] = "Research",
            ["ExecutionProfiles:Profiles:Research:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Research:AllowedTools:0"] = "web_search",
            ["ExecutionProfiles:Profiles:Research:AllowedTools:1"] = "read_memory",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "http_request", "Research");

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not allowed by execution profile");
    }

    [Fact]
    public void IsAuthorized_WhenExecutionProfileAllowsTool_ShouldAllow()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:http_request:Enabled"] = "true",
            ["ToolPermissions:http_request:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:DefaultProfile"] = "Build",
            ["ExecutionProfiles:Profiles:Build:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:AllowedTools:0"] = "http_request",
            ["ExecutionProfiles:Profiles:Build:AllowedTools:1"] = "web_search",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "http_request", "Build");

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void IsAuthorized_WhenExecutionProfileUnknown_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:web_search:Enabled"] = "true",
            ["ToolPermissions:web_search:AllowedRanks"] = "Supreme,Prince,Duke,Worker",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:DefaultProfile"] = "Research",
            ["ExecutionProfiles:Profiles:Research:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Research:AllowedTools:0"] = "web_search",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "web_search", "UnknownProfile");

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not configured");
    }

    [Fact]
    public void IsAuthorized_WhenFileScopeOutsideAllowedScopes_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:fs_write:Enabled"] = "true",
            ["ToolPermissions:fs_write:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:AllowedTools:0"] = "fs_write",
            ["ExecutionProfiles:Profiles:Build:AllowedFileScopes:0"] = "src/**",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);
        var parameters = new Dictionary<string, object> { ["path"] = "README.md" };

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "fs_write", "Build", parameters);

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("outside allowed file scopes");
    }

    [Fact]
    public void IsAuthorized_WhenNetworkScopeOutsideAllowedScopes_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:http_request:Enabled"] = "true",
            ["ToolPermissions:http_request:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:AllowedTools:0"] = "http_request",
            ["ExecutionProfiles:Profiles:Build:AllowedNetworkScopes:0"] = ".example.com",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);
        var parameters = new Dictionary<string, object> { ["url"] = "https://api.not-example.net/v1" };

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "http_request", "Build", parameters);

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("outside allowed network scopes");
    }

    [Fact]
    public void IsAuthorized_WhenCommandNotInAllowlist_ShouldDeny()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["ToolPermissions:python_exec:Enabled"] = "true",
            ["ToolPermissions:python_exec:AllowedRanks"] = "Supreme,Prince,Duke",
            ["ExecutionProfiles:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:Enabled"] = "true",
            ["ExecutionProfiles:Profiles:Build:AllowedTools:0"] = "python_exec",
            ["ExecutionProfiles:Profiles:Build:CommandAllowlist:0"] = "node_exec",
        });

        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        var result = sut.IsAuthorized("a1", "lucifer", AgentRank.Supreme, "python_exec", "Build", new Dictionary<string, object>());

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not allowed by execution profile");
    }

    [Fact]
    public void IsAuthorized_DefaultBuildProfileTools_ShouldBeExecutableForDuke()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "fs_read", "Build").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "http_request", "Build").IsAuthorized.Should().BeTrue();
        sut.IsAuthorized("a1", "vassago", AgentRank.Duke, "python_exec", "Build").IsAuthorized.Should().BeTrue();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
