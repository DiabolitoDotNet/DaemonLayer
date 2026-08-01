using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Maintenance;
using InfernalHierarchy.Memory.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class MemoryBackupServiceTests
{
    [Fact]
    public async Task StartAsync_WhenEnabledAndBackupOnStartup_CreatesBackupFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "infernal-memory-backups", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            var dbPath = Path.Combine(root, "memory.db");
            using (var memory = new LiteDbSharedMemory(
                Options.Create(new MemoryOptions { DatabasePath = dbPath }),
                NullLogger<LiteDbSharedMemory>.Instance))
            {
                await memory.AddDecisionAsync(new Decision
                {
                    CreatedBy = "lucifer",
                    Context = "ctx",
                    Action = "act",
                    Reasoning = "why"
                });

                var backupDirectory = Path.Combine(root, "backups");
                var service = new MemoryBackupService(
                    memory,
                    Options.Create(new MemoryBackupOptions
                    {
                        Enabled = true,
                        BackupOnStartup = true,
                        IntervalHours = 24,
                        DirectoryPath = backupDirectory,
                        MaxBackupFiles = 3,
                        MaxBackupAgeDays = 7
                    }),
                    NullLogger<MemoryBackupService>.Instance);

                await service.StartAsync(CancellationToken.None);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && (!Directory.Exists(backupDirectory) || Directory.GetFiles(backupDirectory, "infernal-memory-*.db").Length == 0))
                {
                    await Task.Delay(50);
                }
                await service.StopAsync(CancellationToken.None);

                Directory.GetFiles(backupDirectory, "infernal-memory-*.db").Should().ContainSingle();
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
