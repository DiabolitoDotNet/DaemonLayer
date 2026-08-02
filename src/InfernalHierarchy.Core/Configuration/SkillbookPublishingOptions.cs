namespace InfernalHierarchy.Core.Configuration;

public sealed class SkillbookPublishingOptions
{
    public bool Enabled { get; set; } = true;

    public string DirectoryPath { get; set; } = "skills/runtime";

    public string DatabasePath { get; set; } = "data/skillbook.db";

    public int PromotionMinSuccessCount { get; set; } = 3;

    public int MaxEntries { get; set; } = 10000;
}
