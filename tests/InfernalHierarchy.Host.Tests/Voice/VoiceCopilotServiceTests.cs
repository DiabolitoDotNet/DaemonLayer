using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests.Voice;

public sealed class VoiceCopilotServiceTests
{
    private sealed class StreamingEmptyClient : ILlmClient, IStreamingLlmClient
    {
        public int NonStreamingCalls { get; private set; }

        public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            NonStreamingCalls++;
            return Task.FromResult("D'accord, peux-tu préciser ?");
        }

        public async IAsyncEnumerable<string> GetStreamingCompletionAsync(
            string systemPrompt,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult("(unused)");
    }

    [Fact]
    public async Task GetReplyAsync_WhenStreamingYieldsNoContent_FallsBackToNonStreaming()
    {
        var client = new StreamingEmptyClient();
        var options = Options.Create(new VoiceCopilotOptions
        {
            Enabled = true,
            SpeakByDefault = false,
            MaxReplyChars = 200,
            MaxTokens = 80,
            Temperature = 0.2,
            MaxHistoryMessages = 2
        });

        var logger = new Mock<ILogger<VoiceCopilotService>>();
        var sut = new VoiceCopilotService(options, client, logger.Object);

        var result = await sut.GetReplyAsync("Bonjour", sessionId: "t1", ct: CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.ReplyText));
        Assert.EndsWith("?", result.ReplyText);
        Assert.Equal(1, client.NonStreamingCalls);
    }

    private sealed class NonStreamingReasoningClient : ILlmClient
    {
        public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            // Simulate a reasoning/meta response that we must not return to the user.
            return Task.FromResult("Bon, l'utilisateur me dit simplement \"Bonjour\". C'est clair : il veut une réponse concise. Je dois répondre.");
        }

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult("(unused)");
    }

    [Fact]
    public async Task GetReplyAsync_WhenModelReturnsReasoningLikeText_ReturnsSafeShortQuestion()
    {
        var options = Options.Create(new VoiceCopilotOptions
        {
            Enabled = true,
            SpeakByDefault = false,
            MaxReplyChars = 200,
            MaxTokens = 80,
            Temperature = 0.2,
            MaxHistoryMessages = 2
        });

        var logger = new Mock<ILogger<VoiceCopilotService>>();
        var sut = new VoiceCopilotService(options, new NonStreamingReasoningClient(), logger.Object);

        var result = await sut.GetReplyAsync("Bonjour", sessionId: "t2", ct: CancellationToken.None);

        Assert.Equal("Bonjour ! Comment puis-je t’aider ?", result.ReplyText);
        Assert.Equal("Bonjour ! Comment puis-je t’aider ?", result.SpeechText);
    }
}
