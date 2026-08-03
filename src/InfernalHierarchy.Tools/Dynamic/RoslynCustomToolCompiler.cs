using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InfernalHierarchy.Tools.Dynamic;

public sealed class RoslynCustomToolCompiler : ICustomToolCompiler
{
    public Task<CustomToolCompileResult> CompileAndCreateAsync(
        string sourceCode,
        string? expectedToolName,
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return Task.FromResult(new CustomToolCompileResult(
                Success: false,
                Tool: null,
                Error: "Source code is empty",
                Diagnostics: Array.Empty<string>()));
        }

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceCode,
                new CSharpParseOptions(LanguageVersion.Latest),
                cancellationToken: ct);

            var references = BuildReferences();

            var assemblyName = "InfernalHierarchy.CustomTools." + Guid.NewGuid().ToString("n");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    concurrentBuild: true,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();

            var emitResult = compilation.Emit(peStream, pdbStream, cancellationToken: ct);
            var diagnostics = emitResult.Diagnostics
                .Select(d => d.ToString())
                .ToList();

            if (!emitResult.Success)
            {
                var error = BuildCompileErrorSummary(emitResult.Diagnostics);
                return Task.FromResult(new CustomToolCompileResult(
                    Success: false,
                    Tool: null,
                    Error: error,
                    Diagnostics: diagnostics));
            }

            peStream.Position = 0;
            pdbStream.Position = 0;

            var alc = new AssemblyLoadContext($"custom-tool:{assemblyName}", isCollectible: true);
            var assembly = alc.LoadFromStream(peStream, pdbStream);

            var created = InfernalHierarchy.Tools.Marketplace.ToolPluginDiscovery.CreateTools(assembly, services, logger);
            if (created.Count == 0)
            {
                return Task.FromResult(new CustomToolCompileResult(
                    Success: false,
                    Tool: null,
                    Error: "Compilation succeeded but no ITool implementations were found in the generated assembly",
                    Diagnostics: diagnostics));
            }

            ITool? tool = null;
            if (!string.IsNullOrWhiteSpace(expectedToolName))
            {
                tool = created.FirstOrDefault(t => string.Equals(t.Name, expectedToolName, StringComparison.OrdinalIgnoreCase));
            }

            tool ??= created[0];

            return Task.FromResult(new CustomToolCompileResult(
                Success: true,
                Tool: tool,
                Error: null,
                Diagnostics: diagnostics));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CustomToolCompileResult(
                Success: false,
                Tool: null,
                Error: ex.Message,
                Diagnostics: Array.Empty<string>()));
        }
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();

        // Reference the platform assemblies provided by the current runtime.
        // This avoids brittle hand-curated reference lists (e.g., missing System.Runtime)
        // and makes dynamic compilation behave consistently across .NET versions.
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(path))
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        void AddAssembly(Assembly assembly)
        {
            if (string.IsNullOrWhiteSpace(assembly.Location)) return;
            refs.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        // Ensure our own assemblies are present even if not part of TPA.
        AddAssembly(typeof(ITool).Assembly);
        AddAssembly(typeof(System.Text.Json.JsonSerializer).Assembly);
        AddAssembly(typeof(Microsoft.Extensions.Logging.ILogger).Assembly);
        AddAssembly(typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilities).Assembly);
        AddAssembly(typeof(System.Net.Http.HttpClient).Assembly);
        AddAssembly(typeof(System.Xml.Linq.XDocument).Assembly);

        // Deduplicate by path
        return refs
            .OfType<PortableExecutableReference>()
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => (MetadataReference)g.First())
            .ToList();
    }

    private static string BuildCompileErrorSummary(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Take(20)
            .ToList();

        if (errors.Count == 0)
        {
            return "Compilation failed";
        }

        return string.Join(Environment.NewLine, errors);
    }
}
