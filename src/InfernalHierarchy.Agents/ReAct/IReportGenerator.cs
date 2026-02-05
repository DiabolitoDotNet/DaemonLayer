namespace InfernalHierarchy.Agents.ReAct;

public interface IReportGenerator
{
    Task<string> GenerateUsageReportAsync(CancellationToken ct);
    Task<string> GenerateModelsReportAsync(CancellationToken ct);
}
