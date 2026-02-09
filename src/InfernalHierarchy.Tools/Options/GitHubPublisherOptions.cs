namespace InfernalHierarchy.Tools.Options;

/// <summary>
/// Configuration for publishing persisted custom tools to GitHub.
/// This is optional and disabled by default.
/// </summary>
public sealed class GitHubPublisherOptions
{
    /// <summary>
    /// Enables the GitHub publishing tool.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// GitHub owner (user or org) where the repository lives.
    /// When empty, the tool requires <c>owner</c> parameter.
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// GitHub username for user-owned publishing.
    /// Optional; when <see cref="Owner"/> is empty, <see cref="Username"/> will be used as the owner.
    /// This exists primarily to support storing the GitHub username in secrets alongside the token.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// GitHub repository name. For monorepo mode, this repo stores all custom tools.
    /// </summary>
    public string Repository { get; set; } = "infernal-custom-tools";

    /// <summary>
    /// Branch to write to.
    /// </summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    /// Root folder inside the repository.
    /// Default is <c>tools</c>.
    /// </summary>
    public string RootPath { get; set; } = "tools";

    /// <summary>
    /// GitHub token (PAT or fine-grained token) with permissions to create private repos and write contents.
    /// Store this in user-secrets or docker secrets; do not commit it.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// When true, creates the private repo if it doesn't exist.
    /// Only supported for user-owned repos (POST /user/repos).
    /// </summary>
    public bool CreateRepoIfMissing { get; set; } = true;
}
