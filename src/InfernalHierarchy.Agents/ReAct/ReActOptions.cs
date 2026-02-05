namespace InfernalHierarchy.Agents.ReAct;

/// <summary>
/// Configuration for the ReAct loop output format and parsing.
/// </summary>
public class ReActOptions
{
    /// <summary>
    /// When enabled, the agent asks the LLM to respond with a single JSON object
    /// containing { thought, action, actionInput }.
    ///
    /// Parsing remains backward-compatible with the legacy Thought/Action/Action Input format.
    /// </summary>
    public bool UseJsonResponse { get; set; } = true;
}
