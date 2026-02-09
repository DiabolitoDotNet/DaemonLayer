namespace InfernalHierarchy.Tools.Dynamic;

public interface ICustomToolCompiler
{
    Task<CustomToolCompileResult> CompileAndCreateAsync(
        string sourceCode,
        string? expectedToolName,
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default);
}
