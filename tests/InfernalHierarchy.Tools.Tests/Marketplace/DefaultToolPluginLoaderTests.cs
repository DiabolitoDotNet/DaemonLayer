using FluentAssertions;
using InfernalHierarchy.Tools.Marketplace;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InfernalHierarchy.Tools.Tests.Marketplace;

public sealed class DefaultToolPluginLoaderTests
{
    [Fact]
    public async Task LoadAssemblyAsync_WhenPluginPathEmpty_ReturnsFailure()
    {
        var sut = new DefaultToolPluginLoader(NullLogger<DefaultToolPluginLoader>.Instance);

        var (assembly, result) = await sut.LoadAssemblyAsync(" ", CancellationToken.None);

        assembly.Should().BeNull();
        result.Loaded.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAssemblyAsync_WhenFileMissing_ReturnsFailure()
    {
        var sut = new DefaultToolPluginLoader(NullLogger<DefaultToolPluginLoader>.Instance);

        var missingPath = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"), "Missing.dll");
        var (assembly, result) = await sut.LoadAssemblyAsync(missingPath, CancellationToken.None);

        assembly.Should().BeNull();
        result.Loaded.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAssemblyAsync_WhenFileIsNotAnAssembly_ReturnsFailure()
    {
        var sut = new DefaultToolPluginLoader(NullLogger<DefaultToolPluginLoader>.Instance);

        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var pluginPath = Path.Combine(tempDir, "NotAnAssembly.dll");
        await File.WriteAllBytesAsync(pluginPath, new byte[] { 1, 2, 3, 4, 5 });

        var (assembly, result) = await sut.LoadAssemblyAsync(pluginPath, CancellationToken.None);

        assembly.Should().BeNull();
        result.Loaded.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAssemblyAsync_WhenValidAssembly_LoadsSuccessfully()
    {
        var sut = new DefaultToolPluginLoader(NullLogger<DefaultToolPluginLoader>.Instance);

        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        // Reuse an already-built assembly from this test run as a known-good DLL.
        var sourceAssemblyPath = typeof(DefaultToolPluginLoader).Assembly.Location;
        sourceAssemblyPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(sourceAssemblyPath).Should().BeTrue();

        var pluginPath = Path.Combine(tempDir, "InfernalHierarchy.Tools.Plugin.dll");
        File.Copy(sourceAssemblyPath, pluginPath, overwrite: true);

        var (assembly, result) = await sut.LoadAssemblyAsync(pluginPath, CancellationToken.None);

        assembly.Should().NotBeNull();
        result.Loaded.Should().BeTrue();
        result.Error.Should().BeNull();
        result.PluginPath.Should().Be(pluginPath);
    }
}
