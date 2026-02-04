using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Service for coordinating multi-agent collaboration and consensus decision-making
/// </summary>
public interface IAgentCollaborationService
{
    /// <summary>
    /// Initiate a collaboration request with multiple agents
    /// </summary>
    /// <param name="request">Collaboration request with task and strategy</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collaboration result with aggregated decision</returns>
    Task<CollaborationResult> RequestCollaborationAsync(
        CollaborationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Submit an agent's response to a collaboration request
    /// </summary>
    /// <param name="requestId">Collaboration request ID</param>
    /// <param name="response">Agent response with decision and confidence</param>
    /// <param name="ct">Cancellation token</param>
    Task SubmitResponseAsync(
        string requestId,
        AgentResponse response,
        CancellationToken ct = default);

    /// <summary>
    /// Get status of a collaboration request
    /// </summary>
    /// <param name="requestId">Collaboration request ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current collaboration request with status</returns>
    Task<CollaborationRequest?> GetCollaborationStatusAsync(
        string requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel an ongoing collaboration request
    /// </summary>
    /// <param name="requestId">Collaboration request ID</param>
    /// <param name="ct">Cancellation token</param>
    Task CancelCollaborationAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Get active collaboration requests for an agent
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of pending collaboration requests</returns>
    Task<List<CollaborationRequest>> GetPendingCollaborationsAsync(
        string agentId,
        CancellationToken ct = default);

    /// <summary>
    /// Get collaboration history for analysis
    /// </summary>
    /// <param name="limit">Maximum number of records</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Recent collaboration requests</returns>
    Task<List<CollaborationRequest>> GetCollaborationHistoryAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Calculate agent weight for weighted voting based on rank, expertise, and past performance
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="agentRank">Agent rank</param>
    /// <param name="toolName">Tool being evaluated (optional)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Weight value (typically 0.5-3.0)</returns>
    Task<double> CalculateAgentWeightAsync(
        string agentId,
        AgentRank agentRank,
        string? toolName = null,
        CancellationToken ct = default);
}
