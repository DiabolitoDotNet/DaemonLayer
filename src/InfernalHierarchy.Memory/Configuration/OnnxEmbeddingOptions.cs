namespace InfernalHierarchy.Memory.Configuration;

/// <summary>
/// Configuration for ONNX embedding service
/// </summary>
public class OnnxEmbeddingOptions
{
    /// <summary>
    /// Whether ONNX embeddings are enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Path to ONNX model file (.onnx)
    /// </summary>
    public string ModelPath { get; set; } = "./models/sentence-transformers/model.onnx";

    /// <summary>
    /// Path to tokenizer config file (tokenizer.json)
    /// </summary>
    public string TokenizerPath { get; set; } = "./models/sentence-transformers/tokenizer.json";

    /// <summary>
    /// Maximum sequence length for tokenization
    /// </summary>
    public int MaxSequenceLength { get; set; } = 128;

    /// <summary>
    /// Embedding dimension (384 for all-MiniLM-L6-v2, 768 for BERT-base)
    /// </summary>
    public int EmbeddingDimension { get; set; } = 384;
}
