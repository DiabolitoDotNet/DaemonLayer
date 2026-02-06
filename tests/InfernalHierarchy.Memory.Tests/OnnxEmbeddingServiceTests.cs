using FluentAssertions;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;
using System.Reflection;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class OnnxEmbeddingServiceTests
{
    private sealed class FakeTokenizer : ITokenizerAdapter
    {
        private readonly int[] _ids;

        public FakeTokenizer(int[] ids)
        {
            _ids = ids;
        }

        public int[] EncodeToIds(string text) => _ids;
    }

    private sealed class FakeSession : IInferenceSession
    {
        private readonly Func<DenseTensor<long>, DenseTensor<long>, float[]> _run;

        public FakeSession(Func<DenseTensor<long>, DenseTensor<long>, float[]> run)
        {
            _run = run;
        }

        public float[] Run(DenseTensor<long> inputIds, DenseTensor<long> attentionMask) => _run(inputIds, attentionMask);

        public void Dispose()
        {
        }
    }

    private sealed class FakeRuntimeFactory : IOnnxRuntimeFactory
    {
        private readonly IInferenceSession _session;
        private readonly ITokenizerAdapter _tokenizer;

        public int CreateSessionCalls { get; private set; }
        public int CreateTokenizerCalls { get; private set; }

        public FakeRuntimeFactory(IInferenceSession session, ITokenizerAdapter tokenizer)
        {
            _session = session;
            _tokenizer = tokenizer;
        }

        public IInferenceSession CreateSession(string modelPath, SessionOptions sessionOptions)
        {
            CreateSessionCalls++;
            return _session;
        }

        public ITokenizerAdapter CreateTokenizer(string tokenizerPath)
        {
            CreateTokenizerCalls++;
            return _tokenizer;
        }
    }

    private static async Task InvokeEnsureInitializedAsync(OnnxEmbeddingService sut)
    {
        var method = typeof(OnnxEmbeddingService)
            .GetMethod("EnsureInitializedAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(sut, Array.Empty<object>())!;
        await task;
    }

    private static bool GetInitialized(OnnxEmbeddingService sut)
    {
        var field = typeof(OnnxEmbeddingService)
            .GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (bool)field!.GetValue(sut)!;
    }

    private static void SetInitialized(OnnxEmbeddingService sut, bool initialized)
    {
        var field = typeof(OnnxEmbeddingService)
            .GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(sut, initialized);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenDisabled_ShouldReturnNormalizedVector()
    {
        // Arrange
        var options = Options.Create(new OnnxEmbeddingOptions
        {
            Enabled = false,
            EmbeddingDimension = 8
        });

        var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        // Act
        var embedding = await sut.GenerateEmbeddingAsync("hello", CancellationToken.None);

        // Assert
        embedding.Should().HaveCount(8);
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.Should().BeApproximately(1.0, 1e-4);

        sut.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenBackendAvailable_ShouldRunInference_MeanPool_AndNormalize()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"model_{Guid.NewGuid()}.onnx");
        var tokenizerPath = Path.Combine(Path.GetTempPath(), $"tokenizer_{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(modelPath, "stub");
        await File.WriteAllTextAsync(tokenizerPath, "stub");

        try
        {
            var options = Options.Create(new OnnxEmbeddingOptions
            {
                Enabled = true,
                ModelPath = modelPath,
                TokenizerPath = tokenizerPath,
                MaxSequenceLength = 3,
                EmbeddingDimension = 2
            });

            var session = new FakeSession((_, __) =>
            {
                // 3 tokens * 2 dims
                // mask should be [1,1,0] due to padding
                return new float[] { 1f, 1f, 3f, 3f, 100f, 100f };
            });
            var tokenizer = new FakeTokenizer([10, 11]);
            var factory = new FakeRuntimeFactory(session, tokenizer);

            using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>(), factory);

            var embedding = await sut.GenerateEmbeddingAsync("hello", CancellationToken.None);

            factory.CreateSessionCalls.Should().Be(1);
            factory.CreateTokenizerCalls.Should().Be(1);

            embedding.Should().HaveCount(2);
            embedding[0].Should().BeApproximately(0.7071f, 1e-3f);
            embedding[1].Should().BeApproximately(0.7071f, 1e-3f);
        }
        finally
        {
            File.Delete(modelPath);
            File.Delete(tokenizerPath);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenInferenceThrows_ShouldFallbackToDeterministicEmbedding()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"model_{Guid.NewGuid()}.onnx");
        var tokenizerPath = Path.Combine(Path.GetTempPath(), $"tokenizer_{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(modelPath, "stub");
        await File.WriteAllTextAsync(tokenizerPath, "stub");

        try
        {
            var options = Options.Create(new OnnxEmbeddingOptions
            {
                Enabled = true,
                ModelPath = modelPath,
                TokenizerPath = tokenizerPath,
                EmbeddingDimension = 8
            });

            var session = new FakeSession((_, __) => throw new InvalidOperationException("boom"));
            var tokenizer = new FakeTokenizer([1, 2, 3]);
            var factory = new FakeRuntimeFactory(session, tokenizer);

            using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>(), factory);

            var e1 = await sut.GenerateEmbeddingAsync("text", CancellationToken.None);
            var e2 = await sut.GenerateEmbeddingAsync("text", CancellationToken.None);

            e1.Should().HaveCount(8);
            e2.Should().Equal(e1);

            var magnitude = Math.Sqrt(e1.Sum(x => x * x));
            magnitude.Should().BeApproximately(1.0, 1e-4);
        }
        finally
        {
            File.Delete(modelPath);
            File.Delete(tokenizerPath);
        }
    }

    [Fact]
    public void DefaultOnnxRuntimeFactory_CreateTokenizer_WhenTokenizerMissing_ShouldThrowFileNotFoundException()
    {
        var factory = new DefaultOnnxRuntimeFactory();

        var missing = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.tokenizer.json");
        var act = () => factory.CreateTokenizer(missing);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DefaultOnnxRuntimeFactory_CreateTokenizer_WhenExtensionUnsupported_ShouldThrowNotSupportedException()
    {
        var factory = new DefaultOnnxRuntimeFactory();
        var path = Path.Combine(Path.GetTempPath(), $"tokenizer_{Guid.NewGuid():N}.bin");

        try
        {
            File.WriteAllText(path, "stub");

            var act = () => factory.CreateTokenizer(path);
            act.Should().Throw<NotSupportedException>();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void DefaultOnnxRuntimeFactory_CreateSession_WhenModelPathBlank_ShouldThrowArgumentException()
    {
        var factory = new DefaultOnnxRuntimeFactory();

        var act = () => factory.CreateSession(" ", new SessionOptions());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultOnnxRuntimeFactory_CreateSession_WhenSessionOptionsNull_ShouldThrowArgumentNullException()
    {
        var factory = new DefaultOnnxRuntimeFactory();

        var act = () => factory.CreateSession("model.onnx", sessionOptions: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultOnnxRuntimeFactory_CreateSession_WhenModelMissing_ShouldThrowFileNotFoundException()
    {
        var factory = new DefaultOnnxRuntimeFactory();
        var modelPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.onnx");

        var act = () => factory.CreateSession(modelPath, new SessionOptions());

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task DefaultOnnxRuntimeFactory_CreateSession_WhenModelFileInvalid_ShouldThrow()
    {
        var factory = new DefaultOnnxRuntimeFactory();
        var modelPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid()}.onnx");

        try
        {
            await File.WriteAllTextAsync(modelPath, "not a real onnx model");

            var act = () => factory.CreateSession(modelPath, new SessionOptions());

            act.Should().Throw<Exception>();
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenModelMissing_ShouldUseFallbackEmbedding()
    {
        // Arrange
        var options = Options.Create(new OnnxEmbeddingOptions
        {
            Enabled = true,
            EmbeddingDimension = 16,
            ModelPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.onnx"),
            TokenizerPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.json")
        });

        var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        // Act
        var e1 = await sut.GenerateEmbeddingAsync("text", CancellationToken.None);
        var e2 = await sut.GenerateEmbeddingAsync("text", CancellationToken.None);

        // Assert (deterministic within process)
        e1.Should().HaveCount(16);
        e2.Should().Equal(e1);

        sut.Dispose();
    }

    [Fact]
    public void PrepareInput_ShouldPadAndTruncate()
    {
        // Arrange
        var options = Options.Create(new OnnxEmbeddingOptions { Enabled = false, EmbeddingDimension = 8 });
        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        var method = typeof(OnnxEmbeddingService)
            .GetMethod("PrepareInput", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        // Act
        var padded = (int[])method!.Invoke(sut, new object[] { new[] { 1, 2, 3 }, 5 })!;
        var truncated = (int[])method!.Invoke(sut, new object[] { new[] { 1, 2, 3, 4, 5, 6 }, 4 })!;
        var exact = new[] { 9, 8 };
        var same = (int[])method!.Invoke(sut, new object[] { exact, 2 })!;

        // Assert
        padded.Should().Equal(new[] { 1, 2, 3, 0, 0 });
        truncated.Should().Equal(new[] { 1, 2, 3, 4 });
        same.Should().Equal(new[] { 9, 8 });
        same.Should().BeSameAs(exact);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenDisabled_ShouldSetInitializedTrue()
    {
        var options = Options.Create(new OnnxEmbeddingOptions
        {
            Enabled = false,
            EmbeddingDimension = 8,
            ModelPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.onnx"),
            TokenizerPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.json")
        });

        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        GetInitialized(sut).Should().BeFalse();

        await InvokeEnsureInitializedAsync(sut);

        GetInitialized(sut).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenEnabledButModelMissing_ShouldSetInitializedTrue()
    {
        var options = Options.Create(new OnnxEmbeddingOptions
        {
            Enabled = true,
            EmbeddingDimension = 8,
            ModelPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.onnx"),
            TokenizerPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.json")
        });

        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        await InvokeEnsureInitializedAsync(sut);

        GetInitialized(sut).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenAlreadyInitialized_ShouldNotTouchModelFiles()
    {
        var options = Options.Create(new OnnxEmbeddingOptions
        {
            Enabled = true,
            EmbeddingDimension = 8,
            ModelPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.onnx"),
            TokenizerPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.json")
        });

        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());
        SetInitialized(sut, initialized: true);

        var e1 = await sut.GenerateEmbeddingAsync("hello", CancellationToken.None);
        var e2 = await sut.GenerateEmbeddingAsync("hello", CancellationToken.None);

        e1.Should().HaveCount(8);
        e2.Should().Equal(e1);
    }

    [Fact]
    public void MeanPooling_ShouldAverageOnlyMaskedTokens()
    {
        // Arrange
        var options = Options.Create(new OnnxEmbeddingOptions { Enabled = false, EmbeddingDimension = 2 });
        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        var method = typeof(OnnxEmbeddingService)
            .GetMethod("MeanPooling", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        // tokenEmbeddings = 3 tokens * 2 dims
        // t0=[1,3], t1=[5,7], t2=[9,11]
        var tokenEmbeddings = new float[] { 1, 3, 5, 7, 9, 11 };
        var attentionMask = new[] { 1, 0, 1 };

        // Act
        var pooled = (float[])method!.Invoke(sut, new object[] { tokenEmbeddings, attentionMask, 2 })!;

        // Assert: average of t0 and t2 => [(1+9)/2, (3+11)/2] = [5,7]
        pooled.Should().Equal(new float[] { 5f, 7f });
    }

    [Fact]
    public void MeanPooling_WhenMaskAllZero_ShouldReturnZeroVector()
    {
        var options = Options.Create(new OnnxEmbeddingOptions { Enabled = false, EmbeddingDimension = 2 });
        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        var method = typeof(OnnxEmbeddingService)
            .GetMethod("MeanPooling", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var tokenEmbeddings = new float[] { 1, 2, 3, 4 };
        var attentionMask = new[] { 0, 0 };

        var pooled = (float[])method!.Invoke(sut, new object[] { tokenEmbeddings, attentionMask, 2 })!;

        pooled.Should().Equal(new float[] { 0f, 0f });
    }

    [Fact]
    public void Normalize_WhenZeroVector_ShouldReturnSameVector()
    {
        // Arrange
        var options = Options.Create(new OnnxEmbeddingOptions { Enabled = false, EmbeddingDimension = 3 });
        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        var method = typeof(OnnxEmbeddingService)
            .GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var input = new float[] { 0f, 0f, 0f };

        // Act
        var normalized = (float[])method!.Invoke(sut, new object[] { input })!;

        // Assert
        normalized.Should().Equal(input);
    }

    [Fact]
    public void Normalize_WhenMagnitudeTiny_ShouldReturnSameVector()
    {
        var options = Options.Create(new OnnxEmbeddingOptions { Enabled = false, EmbeddingDimension = 2 });
        using var sut = new OnnxEmbeddingService(options, Mock.Of<ILogger<OnnxEmbeddingService>>());

        var method = typeof(OnnxEmbeddingService)
            .GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var input = new float[] { 1e-12f, -1e-12f };
        var normalized = (float[])method!.Invoke(sut, new object[] { input })!;

        normalized.Should().Equal(input);
    }
}