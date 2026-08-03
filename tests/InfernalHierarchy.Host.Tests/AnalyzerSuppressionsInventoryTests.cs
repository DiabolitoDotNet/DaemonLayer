using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AnalyzerSuppressionsInventoryTests
{
    [Fact]
    public void SuppressionInventory_ShouldListAllSourceFilesWithSuppressions()
    {
        var repoRoot = FindRepoRoot();
        var inventoryPath = Path.Combine(repoRoot, "Documentation", "Runbooks", "Analyzer-Suppressions-Inventory.md");
        File.Exists(inventoryPath).Should().BeTrue("suppression inventory must exist");

        var inventoryText = File.ReadAllText(inventoryPath);
        var fileEntries = ParseInventoryFileColumn(inventoryText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var suppressionPattern = new Regex("SuppressMessage\\(|#pragma warning disable", RegexOptions.Compiled);
        var suppressedFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => suppressionPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => ToRepoRelative(repoRoot, path))
            .ToArray();

        suppressedFiles.Should().NotBeEmpty();
        foreach (var file in suppressedFiles)
        {
            fileEntries.Should().Contain(file, $"suppression inventory must track {file}");
        }
    }

    private static IEnumerable<string> ParseInventoryFileColumn(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || trimmed.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = trimmed.Split('|', StringSplitOptions.TrimEntries);
            if (columns.Length < 4)
            {
                continue;
            }

            var fileColumn = columns[2];
            if (fileColumn.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
            {
                yield return fileColumn;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InfernalHierarchy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }

    private static string ToRepoRelative(string repoRoot, string fullPath)
        => Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
}
