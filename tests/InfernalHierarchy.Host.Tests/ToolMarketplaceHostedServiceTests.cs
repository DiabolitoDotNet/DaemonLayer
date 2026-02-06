using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Tools.Marketplace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ToolMarketplaceHostedServiceTests
{
    private sealed class FakeToolRegistry : IToolRegistry
    {
        public readonly List<string> RegisteredNames = new();

        public void RegisterTool(ITool tool) => RegisteredNames.Add(tool.Name);

        public ITool? GetTool(string name) => null;
        public IEnumerable<ITool> GetAllTools() => Array.Empty<ITool>();
        public IEnumerable<ITool> GetToolsForAgent(string[] toolNames) => Array.Empty<ITool>();

        public Task<ToolResult> ExecuteToolWithTrackingAsync(
            string toolName,
            Dictionary<string, object> parameters,
            string? agentId = null,
            string? agentRank = null,
            string? agentName = null,
            CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = false, Output = string.Empty, Error = "not implemented" });

        public T? GetService<T>() where T : class => null;
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FakeLoader : IToolPluginLoader
    {
        private readonly Assembly _assembly;
        public int Calls { get; private set; }
        public FakeLoader(Assembly assembly) => _assembly = assembly;

        public Task<(Assembly? Assembly, ToolPluginLoadResult Result)> LoadAssemblyAsync(string pluginPath, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((_assembly, new ToolPluginLoadResult(pluginPath, Loaded: true, ToolCount: 0, Error: null)));
        }
    }

    private sealed class FakeFailingLoader : IToolPluginLoader
    {
        public int Calls { get; private set; }

        public Task<(Assembly? Assembly, ToolPluginLoadResult Result)> LoadAssemblyAsync(string pluginPath, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<(Assembly?, ToolPluginLoadResult)>((null, new ToolPluginLoadResult(pluginPath, Loaded: false, ToolCount: 0, Error: "fail")));
        }
    }

    private sealed class PluginTool : ITool
    {
        public string Name => "plugin_tool";
        public string Description => "d";
        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }

    [Fact]
    public async Task HostedService_WhenEnabled_LoadsToolsFromAllowlistedPlugin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var pluginPath = Path.Combine(tempDir, "MyPlugin.dll");
        await File.WriteAllTextAsync(pluginPath, "placeholder");

        var marketplaceOptions = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir,
            RescanIntervalSeconds = 1
        };
        marketplaceOptions.AllowedPluginFiles.Add("MyPlugin.dll");

        var options = Options.Create(marketplaceOptions);

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options.Value),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StartAsync(cts.Token);
        await Task.Delay(250, cts.Token);
        await sut.StopAsync(cts.Token);

        registry.RegisteredNames.Should().Contain("plugin_tool");
    }

    [Fact]
    public async Task ScanOnce_SkipsBlankAllowlistEntries_AndMissingFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir,
            MaxPluginBytes = 1024,
            RescanIntervalSeconds = 1
        };
        options.AllowedPluginFiles.Add("");
        options.AllowedPluginFiles.Add("Missing.dll");

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);

        loader.Calls.Should().Be(0);
        registry.RegisteredNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanOnce_SkipsPlugin_WhenTooLarge()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var pluginPath = Path.Combine(tempDir, "Big.dll");
        await File.WriteAllBytesAsync(pluginPath, new byte[20]);

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir,
            MaxPluginBytes = 10,
            RescanIntervalSeconds = 1
        };
        options.AllowedPluginFiles.Add("Big.dll");

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);

        loader.Calls.Should().Be(0);
        registry.RegisteredNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanOnce_WhenLoaderFails_RecordsWriteTime_AndSkipsUntilFileChanges()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var pluginPath = Path.Combine(tempDir, "MyPlugin.dll");
        await File.WriteAllBytesAsync(pluginPath, new byte[] { 1, 2, 3 });

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir,
            MaxPluginBytes = 1024,
            RescanIntervalSeconds = 1
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeFailingLoader();

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);
        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);

        loader.Calls.Should().Be(1);
        registry.RegisteredNames.Should().BeEmpty();

        File.SetLastWriteTimeUtc(pluginPath, DateTime.UtcNow.AddMinutes(1));
        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);
        loader.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ScanOnce_WhenWriteTimeUnchanged_DoesNotReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var pluginPath = Path.Combine(tempDir, "MyPlugin.dll");
        await File.WriteAllTextAsync(pluginPath, "placeholder");

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = tempDir,
            MaxPluginBytes = 1024,
            RescanIntervalSeconds = 1
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);
        await InvokeScanOnceAsync(sut, tempDir, options, CancellationToken.None);

        loader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_CancelsDelayOnStop()
    {
        var options = new ToolMarketplaceOptions
        {
            Enabled = false,
            PluginsDirectory = "plugins",
            RescanIntervalSeconds = 1
        };

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_AndDirectoryMissing_CancelsDelayOnStop()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "infernal-marketplace-tests", Guid.NewGuid().ToString("n"), "missing");

        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = missingDir,
            RescanIntervalSeconds = 1
        };
        options.AllowedPluginFiles.Add("MyPlugin.dll");

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ResolvePluginsDirectory_WhenRelative_UsesContentRoot()
    {
        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "plugins"
        };

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Path.Combine(Path.GetTempPath(), "infernal-content-root") };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        var resolved = InvokeResolvePluginsDirectory(sut, "plugins");
        resolved.Should().Be(Path.GetFullPath(Path.Combine(env.ContentRootPath, "plugins")));
    }

    [Fact]
    public void ResolvePluginsDirectory_WhenAbsolute_ReturnsAsIs()
    {
        var options = new ToolMarketplaceOptions
        {
            Enabled = true,
            PluginsDirectory = "plugins"
        };

        var registry = new FakeToolRegistry();
        var env = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        var loader = new FakeLoader(typeof(PluginTool).Assembly);

        var sut = new ToolMarketplaceHostedService(
            registry,
            services: new ServiceCollection().BuildServiceProvider(),
            loader,
            env,
            optionsMonitor: new OptionsMonitorShim<ToolMarketplaceOptions>(options),
            NullLogger<ToolMarketplaceHostedService>.Instance);

        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "infernal-plugins"));
        var resolved = InvokeResolvePluginsDirectory(sut, absolute);
        resolved.Should().Be(absolute);
    }

    private static string InvokeResolvePluginsDirectory(ToolMarketplaceHostedService sut, string configured)
    {
        var method = typeof(ToolMarketplaceHostedService)
            .GetMethod("ResolvePluginsDirectory", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var result = (string?)method!.Invoke(sut, new object[] { configured });
        result.Should().NotBeNullOrWhiteSpace();
        return result!;
    }

    private static Task InvokeScanOnceAsync(ToolMarketplaceHostedService sut, string pluginsDir, ToolMarketplaceOptions options, CancellationToken ct)
    {
        var method = typeof(ToolMarketplaceHostedService)
            .GetMethod("ScanOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var task = (Task?)method!.Invoke(sut, new object[] { pluginsDir, options, ct });
        task.Should().NotBeNull();
        return task!;
    }

    private sealed class OptionsMonitorShim<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public OptionsMonitorShim(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable OnChange(Action<T, string?> listener) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
