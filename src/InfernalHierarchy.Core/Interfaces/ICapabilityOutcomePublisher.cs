namespace InfernalHierarchy.Core.Interfaces;

public interface ICapabilityOutcomePublisher
{
    Task RecordOutcomeAsync(CapabilityOutcome outcome, CancellationToken ct = default);
}
