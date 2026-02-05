namespace InfernalHierarchy.Memory.Configuration;

public class VectorMemoryOptions
{
    public Uri QdrantUrl { get; set; } = new Uri("http://localhost:6333");
    public string CollectionName { get; set; } = "infernal_facts";
    public int VectorDimensions { get; set; } = 384; // Default for sentence-transformers/all-MiniLM-L6-v2
    public bool Enabled { get; set; }
}
