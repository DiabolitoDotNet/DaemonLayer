
namespace InfernalHierarchy.GraphQL;

/// <summary>
/// GraphQL mutation resolver for InfernalHierarchy
/// </summary>
public class Mutation
{
    /// <summary>
    /// Creates a new agent
    /// </summary>
    /// <param name="input">Agent creation input</param>
    /// <param name="agentFactory">Agent factory</param>
    /// <param name="registry">Agent registry</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created agent</returns>
    public async Task<Agent> CreateAgent(
        CreateAgentInput input,
        [Service] IAgentFactory agentFactory,
        [Service] IAgentRegistry registry,
        CancellationToken ct)
    {
        var agent = await agentFactory.CreateAgentAsync(
            input.PersonaName,
            input.ParentAgentId,
            ct).ConfigureAwait(false);

        return agent;
    }

    /// <summary>
    /// Terminates an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="registry">Agent registry</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if terminated</returns>
    public async Task<bool> TerminateAgent(
        string agentId,
        [Service] IAgentRegistry registry,
        CancellationToken ct)
    {
        var agent = registry.GetById(agentId);
        if (agent == null)
        {
            return false;
        }

        await agent.TerminateAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Adds a fact to shared memory
    /// </summary>
    /// <param name="input">Fact input</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created fact</returns>
    public async Task<Fact> AddFact(
        AddFactInput input,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        var fact = new Fact
        {
            Content = input.Content,
            Source = input.Source,
            Confidence = input.Confidence,
            Tags = input.Tags ?? new List<string>()
        };

        await memory.AddFactAsync(fact, ct).ConfigureAwait(false);
        return fact;
    }

    /// <summary>
    /// Adds a decision to shared memory
    /// </summary>
    /// <param name="input">Decision input</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created decision</returns>
    public async Task<Decision> AddDecision(
        AddDecisionInput input,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        var decision = new Decision
        {
            Description = input.Description,
            MadeBy = input.MadeBy,
            Reasoning = input.Reasoning,
            Alternatives = input.Alternatives ?? new List<string>(),
            Impact = input.Impact
        };

        await memory.AddDecisionAsync(decision, ct).ConfigureAwait(false);
        return decision;
    }

    /// <summary>
    /// Creates a task
    /// </summary>
    /// <param name="input">Task input</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created task</returns>
    public async Task<AgentTask> CreateTask(
        CreateTaskInput input,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        var task = new AgentTask
        {
            Description = input.Description,
            AssignedTo = input.AssignedTo,
            Priority = input.Priority,
            Status = TaskStatus.Pending
        };

        await memory.AddTaskAsync(task, ct).ConfigureAwait(false);
        return task;
    }

    /// <summary>
    /// Requests multi-agent collaboration
    /// </summary>
    /// <param name="input">Collaboration input</param>
    /// <param name="collaborationService">Collaboration service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collaboration result</returns>
    public async Task<CollaborationResult> RequestCollaboration(
        CollaborationInput input,
        [Service] IAgentCollaborationService collaborationService,
        CancellationToken ct)
    {
        var request = new CollaborationRequest
        {
            InitiatorAgentId = input.InitiatorAgentId,
            Task = input.Task,
            Strategy = input.Strategy,
            MinimumConfidence = input.MinimumConfidence ?? 0.7,
            MinimumParticipants = input.MinimumParticipants ?? 2,
            Timeout = TimeSpan.FromSeconds(input.TimeoutSeconds ?? 30),
            ParticipantAgentIds = input.ParticipantAgentIds
        };

        return await collaborationService.RequestCollaborationAsync(request, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Input for creating an agent
/// </summary>
public record CreateAgentInput(string PersonaName, string? ParentAgentId);

/// <summary>
/// Input for adding a fact
/// </summary>
public record AddFactInput(string Content, string Source, double Confidence, List<string>? Tags);

/// <summary>
/// Input for adding a decision
/// </summary>
public record AddDecisionInput(
    string Description, 
    string MadeBy, 
    string Reasoning, 
    List<string>? Alternatives, 
    string Impact);

/// <summary>
/// Input for creating a task
/// </summary>
public record CreateTaskInput(string Description, string AssignedTo, int Priority);

/// <summary>
/// Input for collaboration request
/// </summary>
public record CollaborationInput(
    string InitiatorAgentId,
    string Task,
    CollaborationStrategy Strategy,
    List<string> ParticipantAgentIds,
    double? MinimumConfidence,
    int? MinimumParticipants,
    int? TimeoutSeconds);
