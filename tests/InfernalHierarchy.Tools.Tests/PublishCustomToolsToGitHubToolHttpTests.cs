using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Meta;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class PublishCustomToolsToGitHubToolHttpTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRepoExists_ShouldWriteIndexAndToolFiles_UsingContentsApi()
    {
        var defs = new[]
        {
            new CustomToolDefinition
            {
                Id = "t1",
                ToolName = "custom_hello",
                Description = "hello",
                SourceCode = "// tool cs",
                CreatedByAgentId = "lucifer",
                CreatedByAgentName = "Lucifer",
                RequiresManualApproval = false,
                SourceHash = "abc"
            },
            new CustomToolDefinition
            {
                Id = "t2",
                ToolName = "custom_.._escape",
                Description = "escape",
                SourceCode = "// escape",
                CreatedByAgentId = "lucifer",
                CreatedByAgentName = "Lucifer",
                RequiresManualApproval = true,
                SourceHash = "def"
            }
        };

        var store = new InMemoryStore(defs);
        var handler = new GitHubFakeHandler(repoExists: true, allowCreate: false);
        var http = new FakeHttpClientFactory(new HttpClient(handler));

        var tool = new PublishCustomToolsToGitHubTool(
            store,
            http,
            new TestOptionsMonitor<GitHubPublisherOptions>(new GitHubPublisherOptions
            {
                Enabled = true,
                Owner = "me",
                Repository = "infernal-custom-tools",
                Branch = "main",
                RootPath = "tools",
                Token = "token",
                CreateRepoIfMissing = false
            }),
            NullLogger<PublishCustomToolsToGitHubTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        result.Success.Should().BeTrue();

        // We should have written index + (2 files per tool)
        handler.PutRequests.Should().ContainSingle(r => r.Path.EndsWith("/contents/tools/index.json", StringComparison.Ordinal));
        handler.PutRequests.Should().ContainSingle(r => r.Path.EndsWith("/contents/tools/custom_hello/definition.json", StringComparison.Ordinal));
        handler.PutRequests.Should().ContainSingle(r => r.Path.EndsWith("/contents/tools/custom_hello/tool.cs", StringComparison.Ordinal));

        // Ensure we never emit traversal segments.
        handler.PutRequests.Should().NotContain(r => r.Path.Contains("/../", StringComparison.Ordinal));
        handler.PutRequests.Should().NotContain(r => r.Path.Contains("/./", StringComparison.Ordinal));

        // Both tools should be published under the tools root.
        handler.PutRequests.Count(r => r.Path.Contains("/contents/tools/custom_", StringComparison.Ordinal) && r.Path.EndsWith("/definition.json", StringComparison.Ordinal))
            .Should().Be(2);
        handler.PutRequests.Count(r => r.Path.Contains("/contents/tools/custom_", StringComparison.Ordinal) && r.Path.EndsWith("/tool.cs", StringComparison.Ordinal))
            .Should().Be(2);

        // Validate index.json content is a JSON array (decode from GitHub Contents API envelope)
        var indexPut = handler.PutRequests.Single(r => r.Path.EndsWith("/contents/tools/index.json", StringComparison.Ordinal));
        using var putEnvelope = JsonDocument.Parse(indexPut.BodyJson);
        putEnvelope.RootElement.TryGetProperty("content", out var contentEl).Should().BeTrue();
        contentEl.ValueKind.Should().Be(JsonValueKind.String);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(contentEl.GetString()!));
        using var indexBody = JsonDocument.Parse(decoded);
        indexBody.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    private sealed class InMemoryStore : ICustomToolStore
    {
        private readonly IReadOnlyList<CustomToolDefinition> _defs;

        public InMemoryStore(IReadOnlyList<CustomToolDefinition> defs) => _defs = defs;

        public Task UpsertAsync(CustomToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;

        public Task<CustomToolDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult<CustomToolDefinition?>(_defs.FirstOrDefault(d => d.Id == id));

        public Task<CustomToolDefinition?> GetByNameAsync(string toolName, CancellationToken ct = default)
            => Task.FromResult<CustomToolDefinition?>(_defs.FirstOrDefault(d => d.ToolName == toolName));

        public Task<IReadOnlyList<CustomToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(_defs);

        public Task<bool> DeleteByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeleteByNameAsync(string toolName, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private readonly T _current;
        public TestOptionsMonitor(T current) => _current = current;
        public T CurrentValue => _current;
        public T Get(string? name) => _current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class GitHubFakeHandler : HttpMessageHandler
    {
        public sealed record Put(string Path, string BodyJson);

        private readonly bool _repoExists;
        private readonly bool _allowCreate;

        public List<Put> PutRequests { get; } = new();

        public GitHubFakeHandler(bool repoExists, bool allowCreate)
        {
            _repoExists = repoExists;
            _allowCreate = allowCreate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');

            if (request.Method == HttpMethod.Get && pathAndQuery.StartsWith("repos/", StringComparison.Ordinal))
            {
                // repo existence check or contents sha check
                if (pathAndQuery.Contains("/contents/", StringComparison.Ordinal))
                {
                    // No file yet => 404 (so PUT will create without sha)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return _repoExists
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && pathAndQuery == "user/repos")
            {
                return _allowCreate
                    ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (request.Method == HttpMethod.Put && pathAndQuery.StartsWith("repos/", StringComparison.Ordinal) && pathAndQuery.Contains("/contents/", StringComparison.Ordinal))
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                lock (PutRequests)
                {
                    PutRequests.Add(new Put(request.RequestUri!.AbsolutePath, body));
                }

                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
    }
}
