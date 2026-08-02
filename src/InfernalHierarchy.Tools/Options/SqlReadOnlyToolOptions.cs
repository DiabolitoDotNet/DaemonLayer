namespace InfernalHierarchy.Tools.Options;

public sealed class SqlReadOnlyToolOptions
{
    public bool Enabled { get; set; } = false;

    public int CommandTimeoutSeconds { get; set; } = 15;

    public int MaxRows { get; set; } = 200;

    public int MaxCellChars { get; set; } = 2000;

    public bool RequireReadOnly { get; set; } = true;

    public bool AllowConnectionStringFromParameters { get; set; } = false;

    public List<string> AllowedConnectionStrings { get; set; } = new();
}