using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class VoiceApiE2ETests : IClassFixture<InfernalHierarchyTestWebAppFactory>
{
    private readonly InfernalHierarchyTestWebAppFactory _factory;

    public VoiceApiE2ETests(InfernalHierarchyTestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Voice_Transcribe_WhenToolDisabled_ReturnsServerError()
    {
        using var client = _factory.CreateClient();

        var bytes = Encoding.UTF8.GetBytes("not-a-real-audio-file");
        using var content = new MultipartFormDataContent();

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "test.ogg");

        using var response = await client.PostAsync("/api/voice/transcribe", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Voice_Speak_WhenToolDisabled_ReturnsServerError()
    {
        using var client = _factory.CreateClient();

        using var content = new StringContent(
            JsonSerializer.Serialize(new { text = "hello" }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/voice/speak", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Voice_Copilot_ReturnsReply_AndStripsMarkdownForSpeech()
    {
        using var client = _factory.CreateClient();

        var scripted = _factory.Services.GetRequiredService<ScriptedLlmClient>();
        scripted.Enqueue("default", "**Salut** — voici un [lien](https://example.com). Ça te va ?");

        using var content = new StringContent(
            JsonSerializer.Serialize(new { text = "Bonjour", sessionId = "demo", speak = false }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/voice/copilot", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("demo", doc.RootElement.GetProperty("sessionId").GetString());

        var reply = doc.RootElement.GetProperty("reply").GetString() ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.EndsWith("?", reply);

        var speechText = doc.RootElement.GetProperty("speechText").GetString() ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(speechText));
        Assert.DoesNotContain("**", speechText);
        Assert.DoesNotContain("[", speechText);
        Assert.DoesNotContain("]", speechText);

        Assert.False(doc.RootElement.GetProperty("ttsEnqueued").GetBoolean());
    }
}
