using FluentAssertions;
using InfernalHierarchy.Tools.Dynamic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class RoslynCustomToolCompilerTests
{
    [Fact]
    public async Task CompileAndCreateAsync_WithHttpClientReference_ShouldSucceed()
    {
        var source = """
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.CustomTools;

public sealed class CustomHttpGetTool : ITool
{
    public string Name => "custom_http_get";
    public string Description => "Test tool that references HttpClient";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        _ = http.Timeout;
        await Task.Yield();
        return new ToolResult { Success = true, Output = "ok" };
    }
}
""";

        var compiler = new RoslynCustomToolCompiler();
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await compiler.CompileAndCreateAsync(
            source,
            expectedToolName: "custom_http_get",
            services,
            NullLogger.Instance);

        result.Success.Should().BeTrue(result.Error);
        result.Tool.Should().NotBeNull();
        result.Tool!.Name.Should().Be("custom_http_get");
    }
}
