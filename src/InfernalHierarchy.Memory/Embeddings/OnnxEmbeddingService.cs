using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Memory.Embeddings;

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
                _logger.LogInformation("✅ Loaded tokenizer from {Path}", _options.TokenizerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to load tokenizer - ONNX embeddings disabled");
                _logger.LogInformation("📘 Falling back to deterministic embeddings (tokenizer failed to initialize)");
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

    public async Task<OnnxEmbeddingProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitializedAsync();

        var modelLoaded = _session != null;
        var tokenizerLoaded = _tokenizer != null;

        return new OnnxEmbeddingProbeResult(
            Enabled: _options.Enabled,
            ModelPath: _options.ModelPath,
            TokenizerPath: _options.TokenizerPath,
            ModelLoaded: modelLoaded,
            TokenizerLoaded: tokenizerLoaded,
            UsingFallback: !(modelLoaded && tokenizerLoaded),
            EmbeddingDimension: _options.EmbeddingDimension,
            MaxSequenceLength: _options.MaxSequenceLength);
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

    public sealed record OnnxEmbeddingProbeResult(
        bool Enabled,
        string ModelPath,
        string TokenizerPath,
        bool ModelLoaded,
        bool TokenizerLoaded,
        bool UsingFallback,
        int EmbeddingDimension,
        int MaxSequenceLength);

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
    {
        if (string.IsNullOrWhiteSpace(tokenizerPath))
        {
            throw new ArgumentException("Tokenizer path must be provided.", nameof(tokenizerPath));
        }

        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException("Tokenizer file not found.", tokenizerPath);
        }

        var extension = Path.GetExtension(tokenizerPath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return HfWordPieceTokenizerAdapter.FromTokenizerJson(tokenizerPath);
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return HfWordPieceTokenizerAdapter.FromVocabTxt(tokenizerPath);
        }

        throw new NotSupportedException($"Unsupported tokenizer file extension '{extension}'. Use tokenizer.json or vocab.txt.");
    }

    private sealed class InferenceSessionAdapter : IInferenceSession
    {
        private readonly InferenceSession _inner;

        public InferenceSessionAdapter(InferenceSession inner)
        {
            _inner = inner;
        }

        public float[] Run(DenseTensor<long> inputIds, DenseTensor<long> attentionMask)
        {
            var inputs = new List<NamedOnnxValue>();

            if (_inner.InputMetadata.ContainsKey("input_ids"))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", inputIds));
            }

            if (_inner.InputMetadata.ContainsKey("attention_mask"))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask));
            }

            if (_inner.InputMetadata.ContainsKey("token_type_ids"))
            {
                // Many BERT-family models accept token_type_ids; sentence-transformers typically uses a single segment.
                var seqLen = checked((int)attentionMask.Length);
                var tokenTypeIds = new DenseTensor<long>(
                    new long[seqLen],
                    new[] { 1, seqLen });
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));
            }

            using var results = _inner.Run(inputs);

            // Prefer the canonical transformer output name when present.
            var preferred = results.FirstOrDefault(r =>
                string.Equals(r.Name, "last_hidden_state", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, "output_0", StringComparison.OrdinalIgnoreCase));

            return (preferred ?? results.First()).AsEnumerable<float>().ToArray();
        }

        public void Dispose() => _inner.Dispose();
    }
}

internal sealed class HfWordPieceTokenizerAdapter : ITokenizerAdapter
{
    private static readonly Regex _whitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, int> _vocab;
    private readonly string _unkToken;
    private readonly string _continuingSubwordPrefix;
    private readonly bool _lowercase;
    private readonly int _unkId;
    private readonly int? _clsId;
    private readonly int? _sepId;

    private HfWordPieceTokenizerAdapter(
        IReadOnlyDictionary<string, int> vocab,
        string unkToken,
        string continuingSubwordPrefix,
        bool lowercase)
    {
        _vocab = vocab;
        _unkToken = unkToken;
        _continuingSubwordPrefix = continuingSubwordPrefix;
        _lowercase = lowercase;

        if (!_vocab.TryGetValue(_unkToken, out _unkId))
        {
            // If the vocab is malformed, fall back to 0 to avoid crashing.
            _unkId = 0;
        }

        _clsId = _vocab.TryGetValue("[CLS]", out var cls) ? cls : null;
        _sepId = _vocab.TryGetValue("[SEP]", out var sep) ? sep : null;
    }

    public static HfWordPieceTokenizerAdapter FromVocabTxt(string vocabPath)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(vocabPath);
        for (var i = 0; i < lines.Length; i++)
        {
            var token = lines[i].TrimEnd('\r', '\n');
            if (token.Length == 0)
            {
                continue;
            }

            // Keep first occurrence.
            if (!vocab.ContainsKey(token))
            {
                vocab[token] = i;
            }
        }

        return new HfWordPieceTokenizerAdapter(
            vocab,
            unkToken: "[UNK]",
            continuingSubwordPrefix: "##",
            lowercase: true);
    }

    public static HfWordPieceTokenizerAdapter FromTokenizerJson(string tokenizerPath)
    {
        using var stream = File.OpenRead(tokenizerPath);
        using var doc = JsonDocument.Parse(stream);

        // Defaults that match most BERT-family tokenizers.
        var unkToken = "[UNK]";
        var continuingSubwordPrefix = "##";
        var lowercase = true;

        if (doc.RootElement.TryGetProperty("normalizer", out var normalizer) &&
            normalizer.ValueKind == JsonValueKind.Object &&
            normalizer.TryGetProperty("lowercase", out var lc) &&
            (lc.ValueKind == JsonValueKind.True || lc.ValueKind == JsonValueKind.False))
        {
            lowercase = lc.GetBoolean();
        }

        if (!doc.RootElement.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Tokenizer JSON missing 'model' section.");
        }

        if (model.TryGetProperty("unk_token", out var unk) && unk.ValueKind == JsonValueKind.String)
        {
            unkToken = unk.GetString() ?? unkToken;
        }

        if (model.TryGetProperty("continuing_subword_prefix", out var prefix) && prefix.ValueKind == JsonValueKind.String)
        {
            continuingSubwordPrefix = prefix.GetString() ?? continuingSubwordPrefix;
        }

        if (!model.TryGetProperty("vocab", out var vocabElement) || vocabElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Tokenizer JSON missing 'model.vocab' object.");
        }

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in vocabElement.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var id))
            {
                vocab[entry.Name] = id;
            }
        }

        if (vocab.Count == 0)
        {
            throw new InvalidOperationException("Tokenizer JSON contained an empty vocab.");
        }

        return new HfWordPieceTokenizerAdapter(vocab, unkToken, continuingSubwordPrefix, lowercase);
    }

    public int[] EncodeToIds(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<int>();
        }

        var normalized = _whitespaceRegex.Replace(text, " ").Trim();
        if (_lowercase)
        {
            normalized = normalized.ToLowerInvariant();
        }

        var tokens = BasicTokenize(normalized);
        var wordPieces = new List<string>(capacity: tokens.Count * 2);

        if (_clsId.HasValue)
        {
            wordPieces.Add("[CLS]");
        }

        foreach (var token in tokens)
        {
            foreach (var piece in WordPieceTokenize(token))
            {
                wordPieces.Add(piece);
            }
        }

        if (_sepId.HasValue)
        {
            wordPieces.Add("[SEP]");
        }

        var ids = new int[wordPieces.Count];
        for (var i = 0; i < wordPieces.Count; i++)
        {
            if (_vocab.TryGetValue(wordPieces[i], out var id))
            {
                ids[i] = id;
            }
            else
            {
                ids[i] = _unkId;
            }
        }

        return ids;
    }

    private List<string> BasicTokenize(string text)
    {
        var tokens = new List<string>();
        var buffer = new char[text.Length];
        var bufferLen = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                buffer[bufferLen++] = ch;
                continue;
            }

            // Punctuation becomes its own token.
            Flush();
            tokens.Add(ch.ToString());
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (bufferLen <= 0)
            {
                return;
            }

            tokens.Add(new string(buffer, 0, bufferLen));
            bufferLen = 0;
        }
    }

    private IEnumerable<string> WordPieceTokenize(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            yield break;
        }

        const int maxInputCharsPerWord = 100;
        if (token.Length > maxInputCharsPerWord)
        {
            yield return _unkToken;
            yield break;
        }

        var start = 0;
        var isBad = false;
        var subTokens = new List<string>();

        while (start < token.Length)
        {
            var end = token.Length;
            string? curSubstr = null;

            while (start < end)
            {
                var substr = token.Substring(start, end - start);
                var candidate = start > 0 ? _continuingSubwordPrefix + substr : substr;

                if (_vocab.ContainsKey(candidate))
                {
                    curSubstr = candidate;
                    break;
                }

                end -= 1;
            }

            if (curSubstr == null)
            {
                isBad = true;
                break;
            }

            subTokens.Add(curSubstr);
            start = end;
        }

        if (isBad)
        {
            yield return _unkToken;
            yield break;
        }

        foreach (var st in subTokens)
        {
            yield return st;
        }
    }
}
