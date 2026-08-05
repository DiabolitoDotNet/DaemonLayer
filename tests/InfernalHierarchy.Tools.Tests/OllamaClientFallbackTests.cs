using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class OllamaClientFallbackTests
{
    [Fact]
    public async Task GetCompletionAsync_WhenDefaultModelRefusesByPolicy_ShouldRetryWithAlternativeModel()
    {
        var handler = new SequenceHandler(
            CreateOkResponse("I can't assist with that request."),
            CreateOkResponse("Alternative model response"));

        var client = CreateClient(handler, new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434/v1"),
            DefaultModel = "qwen3:8b",
            AlternativeModel = "dolphin3:8b"
        });

        var result = await client.GetCompletionAsync("sys", "user");

        result.Should().Be("Alternative model response");
        handler.RequestedModels.Should().Equal("qwen3:8b", "dolphin3:8b");
    }

    [Fact]
    public async Task GetCompletionAsync_WhenDefaultModelReturnsNormalResponse_ShouldNotFallback()
    {
        var handler = new SequenceHandler(CreateOkResponse("Normal response from default model."));

        var client = CreateClient(handler, new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434/v1"),
            DefaultModel = "qwen3:8b",
            AlternativeModel = "dolphin3:8b"
        });

        var result = await client.GetCompletionAsync("sys", "user");

        result.Should().Be("Normal response from default model.");
        handler.RequestedModels.Should().Equal("qwen3:8b");
    }

    [Fact]
    public async Task GetCompletionAsync_WhenDefaultModelIsPolicyBlocked_ShouldRetryWithAlternativeModel()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"blocked by safety policy\"}}", Encoding.UTF8, "application/json")
            },
            CreateOkResponse("Alternative model response"));

        var client = CreateClient(handler, new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434/v1"),
            DefaultModel = "qwen3:8b",
            AlternativeModel = "dolphin3:8b"
        });

        var result = await client.GetCompletionAsync("sys", "user");

        result.Should().Be("Alternative model response");
        handler.RequestedModels.Should().Equal("qwen3:8b", "dolphin3:8b");
    }

    private static OllamaClient CreateClient(HttpMessageHandler handler, OllamaOptions options)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(x => x.CreateClient(nameof(OllamaClient)))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var monitor = new Mock<IOptionsMonitor<OllamaOptions>>(MockBehavior.Strict);
        monitor.SetupGet(x => x.CurrentValue).Returns(options);

        return new OllamaClient(
            factory.Object,
            monitor.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<OllamaClient>>());
    }

    private static HttpResponseMessage CreateOkResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"choices\":[{{\"message\":{{\"content\":\"{EscapeJson(content)}\"}}}}]}}",
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string> RequestedModels { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var payload = await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("model", out var modelElement) &&
                    modelElement.ValueKind == JsonValueKind.String)
                {
                    RequestedModels.Add(modelElement.GetString() ?? string.Empty);
                }
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more HTTP responses configured for SequenceHandler.");
            }

            return _responses.Dequeue();
        }
    }
}
