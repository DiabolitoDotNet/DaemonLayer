using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Tools;
using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class SkillbookOutcomePublisherTests
{
    [Fact]
    public async Task RecordOutcomeAsync_ShouldPublishAfterPromotionThreshold_WithProvenance()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-skillbook-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "skillbook.db");
        var outputDir = Path.Combine(tempDir, "runtime-skills");

        using var publisher = new SkillbookOutcomePublisher(
            Options.Create(new SkillbookPublishingOptions
            {
                Enabled = true,
                DatabasePath = dbPath,
                DirectoryPath = outputDir,
                PromotionMinSuccessCount = 3,
                MaxEntries = 100
            }),
            Options.Create(new MemoryOptions { DatabasePath = dbPath }),
            NullLogger<SkillbookOutcomePublisher>.Instance);

        for (var i = 0; i < 3; i++)
        {
            await publisher.RecordOutcomeAsync(new CapabilityOutcome
            {
                Kind = CapabilityOutcomeKind.CustomToolExecutionSucceeded,
                CapabilityId = "custom_data_fetch",
                CapabilityType = "custom_tool",
                SourceTask = "fetch remote data safely",
                RiskLevel = "Medium",
                AgentId = "lucifer",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        var filePath = Path.Combine(outputDir, "custom_data_fetch.json");
        File.Exists(filePath).Should().BeTrue();

        var payload = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        payload.RootElement.GetProperty("id").GetString().Should().Be("custom_data_fetch");
        payload.RootElement.GetProperty("capability_type").GetString().Should().Be("custom_tool");

        var provenance = payload.RootElement.GetProperty("provenance");
        provenance.GetProperty("source_task").GetString().Should().Be("fetch remote data safely");
        provenance.GetProperty("risk_level").GetString().Should().Be("Medium");
        provenance.GetProperty("success_count").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        provenance.GetProperty("last_validated_date").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RecordOutcomeAsync_ShouldIgnoreInvalidCapabilityMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-skillbook-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "skillbook.db");
        var outputDir = Path.Combine(tempDir, "runtime-skills");

        using var publisher = new SkillbookOutcomePublisher(
            Options.Create(new SkillbookPublishingOptions
            {
                Enabled = true,
                DatabasePath = dbPath,
                DirectoryPath = outputDir,
                PromotionMinSuccessCount = 1,
                MaxEntries = 100
            }),
            Options.Create(new MemoryOptions { DatabasePath = dbPath }),
            NullLogger<SkillbookOutcomePublisher>.Instance);

        await publisher.RecordOutcomeAsync(new CapabilityOutcome
        {
            Kind = CapabilityOutcomeKind.CustomToolCreated,
            CapabilityId = string.Empty,
            CapabilityType = string.Empty,
            SourceTask = "n/a",
            RiskLevel = "Low",
            AgentId = "lucifer",
            OccurredAtUtc = DateTime.UtcNow
        });

        Directory.Exists(outputDir).Should().BeFalse();
    }
}
