namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Supervises active agent trees and can intervene when agents are stuck, looping, or over budget.
/// Typical interventions include requesting a global re-plan from a root agent and preempting/terminating runaway sub-agents.
/// </summary>
public interface IAgentSupervisor
{
    /// <summary>
    /// Requests that the specified root agent performs a global re-plan.
    /// </summary>
    /// <param name="rootAgentId">Root agent id (typically Supreme/Prince).</param>
    /// <param name="reason">Human-readable reason for the re-plan request.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RequestReplanAsync(string rootAgentId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Preempts a specific agent (usually by terminating it and letting orchestration recreate it if needed).
    /// </summary>
    /// <param name="agentId">Agent id to preempt.</param>
    /// <param name="reason">Human-readable reason for preemption.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PreemptAgentAsync(string agentId, string reason, CancellationToken ct = default);
}
