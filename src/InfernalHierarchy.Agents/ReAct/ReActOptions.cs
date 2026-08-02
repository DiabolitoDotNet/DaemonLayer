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

    /// <summary>
    /// List of tool names that, when executed successfully, should immediately stop the ReAct loop.
    /// This prevents post-success hallucinations and duplicate tool invocations (e.g., sending the
    /// same email multiple times).
    /// </summary>
    public string[] TerminalTools { get; set; } = ["email_send", "send_telegram", "create_custom_tool"];

    /// <summary>
    /// Upper hard safety bound for ReAct iterations.
    /// </summary>
    public int HardMaxIterations { get; set; } = 8;

    /// <summary>
    /// Iteration budget for low-complexity tasks.
    /// </summary>
    public int SimpleTaskMaxIterations { get; set; } = 3;

    /// <summary>
    /// Iteration budget for medium-complexity tasks.
    /// </summary>
    public int MediumTaskMaxIterations { get; set; } = 5;

    /// <summary>
    /// Iteration budget for high-complexity tasks.
    /// </summary>
    public int ComplexTaskMaxIterations { get; set; } = 8;

    /// <summary>
    /// Soft planning hint for collaboration fanout.
    /// </summary>
    public int MaxParallelBranches { get; set; } = 3;
}
