using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System.Text.Json;

namespace InfernalHierarchy.Memory;

/// <summary>
/// ONNX-based sentence embedding service using sentence-transformers models
/// </summary>
public sealed class OnnxEmbeddingService : IDisposable
{
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly OnnxEmbeddingOptions _options;
    private IInferenceSession? _session;
    private ITokenizerAdapter? _tokenizer;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IOnnxRuntimeFactory _runtimeFactory;

    public OnnxEmbeddingService(
        IOptions<OnnxEmbeddingOptions> options,
        ILogger<OnnxEmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _tokenizer = null;
        _runtimeFactory = new DefaultOnnxRuntimeFactory();
    }

    internal OnnxEmbeddingService(
        IOptions<OnnxEmbeddingOptions> options,
        ILogger<OnnxEmbeddingService> logger,
        IOnnxRuntimeFactory runtimeFactory)
    {
        _options = options.Value;
        _logger = logger;
        _tokenizer = null;
        _runtimeFactory = runtimeFactory;
    }

    /// <summary>
    /// Initialize ONNX model and tokenizer (lazy initialization)
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            if (!_options.Enabled)
            {
                _logger.LogWarning("⚠️ ONNX embeddings disabled - will use fallback");
                _initialized = true;
                return;
            }

            // Check if model file exists
            if (!File.Exists(_options.ModelPath))
            {
                _logger.LogWarning("⚠️ ONNX model not found at {Path} - will use fallback embeddings", _options.ModelPath);
                _initialized = true;
                return;
            }

            // Load ONNX model
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            try
            {
                _session = _runtimeFactory.CreateSession(_options.ModelPath, sessionOptions);
                _logger.LogInformation("✅ Loaded ONNX model from {Path}", _options.ModelPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to load ONNX model - will use fallback embeddings");
                _initialized = true;
                return;
            }

            // Load tokenizer
            if (!File.Exists(_options.TokenizerPath))
            {
                _logger.LogWarning("⚠️ Tokenizer not found at {Path} - ONNX embeddings disabled", _options.TokenizerPath);
                _initialized = true;
                return;
            }

            try
            {
                _tokenizer = _runtimeFactory.CreateTokenizer(_options.TokenizerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to load tokenizer - ONNX embeddings disabled");
                _logger.LogInformation("📘 Fallback to deterministic embeddings until tokenizer API is updated");
                _initialized = true;
                return;
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Generate embedding for text using ONNX model
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();

        if (_session == null || _tokenizer == null)
        {
            // Fallback to deterministic hash-based embeddings
            return GenerateFallbackEmbedding(text);
        }

        try
        {
            // Tokenize input
            var inputIds = _tokenizer.EncodeToIds(text);
            var attentionMask = Enumerable.Repeat(1, inputIds.Length).ToArray();

            // Truncate or pad to model's max length
            var maxLength = _options.MaxSequenceLength;
            inputIds = PrepareInput(inputIds, maxLength);
            attentionMask = PrepareInput(attentionMask, maxLength);

            // Create input tensors
            var inputIdsTensor = new DenseTensor<long>(
                inputIds.Select(i => (long)i).ToArray(),
                new[] { 1, inputIds.Length });

            var attentionMaskTensor = new DenseTensor<long>(
                attentionMask.Select(i => (long)i).ToArray(),
                new[] { 1, attentionMask.Length });

            // Run inference
            var output = _session.Run(inputIdsTensor, attentionMaskTensor);

            // Mean pooling over token embeddings
            var embedding = MeanPooling(output, attentionMask, _options.EmbeddingDimension);

            // Normalize
            var normalized = Normalize(embedding);

            _logger.LogDebug("Generated {Dim}D embedding for text: {Preview}",
                normalized.Length, text.Length > 50 ? text[..50] + "..." : text);

            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate ONNX embedding, using fallback");
            return GenerateFallbackEmbedding(text);
        }
    }

    /// <summary>
    /// Prepare input by truncating or padding to target length
    /// </summary>
    private int[] PrepareInput(int[] input, int targetLength)
    {
        if (input.Length > targetLength)
        {
            return input.Take(targetLength).ToArray();
        }

        if (input.Length < targetLength)
        {
            var padded = new int[targetLength];
            Array.Copy(input, padded, input.Length);
            return padded;
        }

        return input;
    }

    /// <summary>
    /// Mean pooling over token embeddings with attention mask
    /// </summary>
    private float[] MeanPooling(float[] tokenEmbeddings, int[] attentionMask, int embeddingDim)
    {
        var numTokens = tokenEmbeddings.Length / embeddingDim;
        var result = new float[embeddingDim];

        var sumMask = attentionMask.Sum();
        if (sumMask == 0)
        {
            return result;
        }

        for (int i = 0; i < numTokens; i++)
        {
            if (attentionMask[i] == 1)
            {
                for (int j = 0; j < embeddingDim; j++)
                {
                    result[j] += tokenEmbeddings[i * embeddingDim + j];
                }
            }
        }

        for (int j = 0; j < embeddingDim; j++)
        {
            result[j] /= sumMask;
        }

        return result;
    }

    /// <summary>
    /// Normalize embedding to unit vector
    /// </summary>
    private float[] Normalize(float[] embedding)
    {
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude < 1e-8)
        {
            return embedding;
        }

        return embedding.Select(x => x / (float)magnitude).ToArray();
    }

    /// <summary>
    /// Fallback deterministic embedding based on text hash
    /// </summary>
    private float[] GenerateFallbackEmbedding(string text)
    {
        var random = new Random(text.GetHashCode());
        var embedding = new float[_options.EmbeddingDimension];

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        return Normalize(embedding);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initLock?.Dispose();
    }
}

internal interface ITokenizerAdapter
{
    int[] EncodeToIds(string text);
}

internal interface IInferenceSession : IDisposable
{
    float[] Run(DenseTensor<long> inputIds, DenseTensor<long> attentionMask);
}

internal interface IOnnxRuntimeFactory
{
    IInferenceSession CreateSession(string modelPath, SessionOptions sessionOptions);
    ITokenizerAdapter CreateTokenizer(string tokenizerPath);
}

internal sealed class DefaultOnnxRuntimeFactory : IOnnxRuntimeFactory
{
    public IInferenceSession CreateSession(string modelPath, SessionOptions sessionOptions)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided.", nameof(modelPath));
        }

        if (sessionOptions is null)
        {
            throw new ArgumentNullException(nameof(sessionOptions));
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX model not found.", modelPath);
        }

        return new InferenceSessionAdapter(new InferenceSession(modelPath, sessionOptions));
    }

    public ITokenizerAdapter CreateTokenizer(string tokenizerPath)
        => throw new NotSupportedException(
            "Microsoft.ML.Tokenizers 1.0.0 tokenizer loading is not yet supported by this service.");

    private sealed class InferenceSessionAdapter : IInferenceSession
    {
        private readonly InferenceSession _inner;

        public InferenceSessionAdapter(InferenceSession inner)
        {
            _inner = inner;
        }

        public float[] Run(DenseTensor<long> inputIds, DenseTensor<long> attentionMask)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };

            using var results = _inner.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
        }

        public void Dispose() => _inner.Dispose();
    }
}

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
