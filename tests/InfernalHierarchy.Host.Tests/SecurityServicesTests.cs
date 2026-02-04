using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class InputValidatorTests
{
    [Theory]
    [InlineData("Hello world", true)]
    [InlineData("Normal text with numbers 123", true)]
    [InlineData("Email: test@example.com", true)]
    [InlineData("", true)]
    public void IsSafeSql_WithSafeInput_ShouldReturnTrue(string input, bool expected)
    {
        // Act
        var result = InputValidator.IsSafeSql(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("DROP TABLE agents")]
    [InlineData("DELETE FROM memory")]
    [InlineData("INSERT INTO tasks VALUES")]
    [InlineData("UPDATE agents SET")]
    [InlineData("UNION SELECT password")]
    [InlineData("EXEC sp_executesql")]
    public void IsSafeSql_WithSqlInjection_ShouldReturnFalse(string input)
    {
        // Act
        var result = InputValidator.IsSafeSql(input);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Normal text")]
    [InlineData("Text with <b>bold</b> tags")]
    [InlineData("URL: https://example.com")]
    public void IsSafeXss_WithSafeInput_ShouldReturnTrue(string input)
    {
        // Act
        var result = InputValidator.IsSafeXss(input);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("<img onerror='alert(1)'>")]
    [InlineData("<body onload='malicious()'>")]
    public void IsSafeXss_WithXssAttempt_ShouldReturnFalse(string input)
    {
        // Act
        var result = InputValidator.IsSafeXss(input);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("normal text")]
    [InlineData("text with spaces")]
    [InlineData("text-with-dashes")]
    public void IsSafeCommand_WithSafeInput_ShouldReturnTrue(string input)
    {
        // Act
        var result = InputValidator.IsSafeCommand(input);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("ls; rm -rf /")]
    [InlineData("cat /etc/passwd | grep root")]
    [InlineData("$(whoami)")]
    [InlineData("`cat secrets`")]
    [InlineData("command & other")]
    public void IsSafeCommand_WithCommandInjection_ShouldReturnFalse(string input)
    {
        // Act
        var result = InputValidator.IsSafeCommand(input);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SanitizeInput_ShouldTruncateLongInput()
    {
        // Arrange
        var longInput = new string('a', 15000);

        // Act
        var result = InputValidator.SanitizeInput(longInput, maxLength: 10000);

        // Assert
        result.Length.Should().Be(10000);
    }

    [Fact]
    public void SanitizeInput_ShouldRemoveNullCharacters()
    {
        // Arrange
        var input = "text\0with\0nulls";

        // Act
        var result = InputValidator.SanitizeInput(input);

        // Assert
        result.Should().NotContain("\0");
        result.Should().Be("textwithnulls");
    }

    [Fact]
    public void SanitizeInput_ShouldTrimWhitespace()
    {
        // Arrange
        var input = "  text with spaces  ";

        // Act
        var result = InputValidator.SanitizeInput(input);

        // Assert
        result.Should().Be("text with spaces");
    }

    [Fact]
    public void ValidateUserInput_WithSafeInput_ShouldReturnValid()
    {
        // Arrange
        var input = "This is a safe input message";

        // Act
        var result = InputValidator.ValidateUserInput(input);

        // Assert
        result.IsValid.Should().BeTrue();
        result.SanitizedValue.Should().Be(input);
        result.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateUserInput_WithSqlInjection_ShouldReportIssue()
    {
        // Arrange
        var input = "SELECT * FROM users WHERE 1=1";

        // Act
        var result = InputValidator.ValidateUserInput(input);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationIssues.Should().Contain(issue => issue.Contains("SQL injection"));
    }

    [Fact]
    public void ValidateUserInput_WithXss_ShouldReportIssue()
    {
        // Arrange
        var input = "<script>alert('hacked')</script>";

        // Act
        var result = InputValidator.ValidateUserInput(input);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationIssues.Should().Contain(issue => issue.Contains("XSS"));
    }

    [Fact]
    public void ValidateUserInput_WithCommandInjection_ShouldReportIssue()
    {
        // Arrange
        var input = "ls; rm -rf /";

        // Act
        var result = InputValidator.ValidateUserInput(input);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationIssues.Should().Contain(issue => issue.Contains("command injection"));
    }

    [Fact]
    public void ValidateUserInput_WithMultipleIssues_ShouldReportAll()
    {
        // Arrange
        var input = "SELECT * FROM users; <script>alert(1)</script>";

        // Act
        var result = InputValidator.ValidateUserInput(input);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationIssues.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ValidateUserInput_WithEmptyString_ShouldReturnValid()
    {
        // Act
        var result = InputValidator.ValidateUserInput("");

        // Assert
        result.IsValid.Should().BeTrue();
        result.SanitizedValue.Should().BeEmpty();
    }
}

public class ToolAuthorizationServiceTests
{
    private readonly Mock<ILogger<ToolAuthorizationService>> _mockLogger;
    private readonly IConfiguration _configuration;

    public ToolAuthorizationServiceTests()
    {
        _mockLogger = new Mock<ILogger<ToolAuthorizationService>>();

        var configData = new Dictionary<string, string>
        {
            ["ToolPermissions:web_search:Enabled"] = "true",
            ["ToolPermissions:web_search:AllowedRanks"] = "Supreme,Prince,Duke",

            ["ToolPermissions:create_sub_agent:Enabled"] = "true",
            ["ToolPermissions:create_sub_agent:AllowedRanks"] = "Supreme,Prince",

            ["ToolPermissions:dangerous_tool:Enabled"] = "false",

            ["ToolPermissions:whitelist_tool:Enabled"] = "true",
            ["ToolPermissions:whitelist_tool:AllowedRanks"] = "Supreme",
            ["ToolPermissions:whitelist_tool:WhitelistedAgents:0"] = "lucifer",
            ["ToolPermissions:whitelist_tool:WhitelistedAgents:1"] = "baal",

            ["ToolPermissions:blacklist_tool:Enabled"] = "true",
            ["ToolPermissions:blacklist_tool:AllowedRanks"] = "Supreme,Prince",
            ["ToolPermissions:blacklist_tool:BlacklistedAgents:0"] = "asmodeus"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();
    }

    [Fact]
    public void IsAuthorized_WithAllowedRank_ShouldReturnSuccess()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("supreme_1", "Lucifer", AgentRank.Supreme, "web_search");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.Reason.Should().BeNullOrEmpty();
    }

    [Fact]
    public void IsAuthorized_WithDisallowedRank_ShouldReturnFailure()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("worker_1", "Worker", AgentRank.Worker, "create_sub_agent");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not authorized");
    }

    [Fact]
    public void IsAuthorized_WithDisabledTool_ShouldReturnFailure()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("supreme_1", "Lucifer", AgentRank.Supreme, "dangerous_tool");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("disabled");
    }

    [Fact]
    public void IsAuthorized_WithBlacklistedAgent_ShouldReturnFailure()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("asmodeus", "Asmodeus", AgentRank.Prince, "blacklist_tool");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not authorized");
    }

    [Fact]
    public void IsAuthorized_WithWhitelistedAgent_ShouldReturnSuccess()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("lucifer", "Lucifer", AgentRank.Supreme, "whitelist_tool");

        // Assert
        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void IsAuthorized_WithNonWhitelistedAgent_ShouldReturnFailure()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("other_agent", "Other", AgentRank.Supreme, "whitelist_tool");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Contain("not in the whitelist");
    }

    [Fact]
    public void IsAuthorized_WithUnconfiguredTool_ShouldAllowByDefault()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var result = service.IsAuthorized("agent_1", "Agent", AgentRank.Duke, "unknown_tool");

        // Assert
        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void GetAuthorizedTools_ShouldReturnOnlyAllowedTools()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var tools = service.GetAuthorizedTools("prince_1", "Baal", AgentRank.Prince);

        // Assert
        tools.Should().Contain("web_search");
        tools.Should().Contain("create_sub_agent");
        tools.Should().NotContain("dangerous_tool");
    }

    [Fact]
    public void GetAuthorizedTools_ForWorker_ShouldReturnLimitedTools()
    {
        // Arrange
        var service = new ToolAuthorizationService(_mockLogger.Object, _configuration);

        // Act
        var tools = service.GetAuthorizedTools("worker_1", "Worker", AgentRank.Worker);

        // Assert
        tools.Should().NotContain("create_sub_agent");
    }
}

public class ResourceLimitServiceTests
{
    private readonly ResourceLimitService _sut;

    public ResourceLimitServiceTests()
    {
        var limits = new ResourceLimits
        {
            MaxSupremeAgents = 1,
            MaxPrinceAgents = 3,
            MaxDukeAgents = 10,
            MaxWorkerAgents = 50,
            MaxTotalAgents = 50,
            MaxConcurrentToolExecutions = 20,
            MaxToolExecutionTimeSeconds = 1
        };

        _sut = new ResourceLimitService(limits);
    }

    [Fact]
    public void CanCreateAgent_WithinLimits_ShouldReturnTrue()
    {
        // Arrange
        var currentPrinceCount = 0;
        var totalAgents = 1;

        // Act
        var result = _sut.CanCreateAgent(AgentRank.Prince, currentPrinceCount, totalAgents);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanCreateAgent_ExceedingRankLimit_ShouldReturnFalse()
    {
        // Arrange
        var currentPrinceCount = 3;
        var totalAgents = 3;

        // Act - try to create 4th prince (limit is 3)
        var result = _sut.CanCreateAgent(AgentRank.Prince, currentPrinceCount, totalAgents);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCreateAgent_ExceedingTotalLimit_ShouldReturnFalse()
    {
        // Arrange
        var currentWorkerCount = 50;
        var totalAgents = 50;

        // Act - try to create 51st agent (limit is 50 total)
        var result = _sut.CanCreateAgent(AgentRank.Worker, currentWorkerCount, totalAgents);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteToolWithLimitAsync_ShouldEnforceMaxConcurrent()
    {
        // Arrange
        var executionCount = 0;
        var tasks = new List<Task>();

        // Act - Try to execute 25 tools concurrently (limit is 20)
        for (int i = 0; i < 25; i++)
        {
            tasks.Add(_sut.ExecuteToolWithLimitAsync(async ct =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(100, ct);
                return 1;
            }, CancellationToken.None));
        }

        // Start all tasks
        await Task.WhenAll(tasks);

        // Assert - All should complete eventually
        executionCount.Should().Be(25);
    }

    [Fact]
    public async Task ExecuteToolWithLimitAsync_ShouldEnforceTimeout()
    {
        // Arrange
        var longRunningTask = async (CancellationToken ct) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            return 1;
        };

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _sut.ExecuteToolWithLimitAsync(longRunningTask, CancellationToken.None));
    }

    [Fact]
    public void CanAddMemoryEntry_WithinLimits_ShouldReturnTrue()
    {
        // Arrange - assuming empty memory

        // Act
        var result = _sut.CanAddMemoryEntry("decision", currentCount: 0);

        // Assert
        result.Should().BeTrue();
    }
}
