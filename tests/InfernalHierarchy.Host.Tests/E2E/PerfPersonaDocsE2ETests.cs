using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class PerfPersonaDocsE2ETests
{
    [Theory]
    [InlineData("/ui/perf")]
    [InlineData("/ui/personas")]
    [InlineData("/ui/docs")]
    public async Task Ui_Pages_ReturnHtml(string path)
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(path);
        res.EnsureSuccessStatusCode();

        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("InfernalHierarchy UI");
    }

    [Fact]
    public async Task Perf_Snapshot_ReturnsJson()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/perf/snapshot");
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("workingSetMB");
    }

    [Fact]
    public async Task Docs_Markdown_ReturnsText()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/docs/markdown");
        res.EnsureSuccessStatusCode();

        var md = await res.Content.ReadAsStringAsync();
        md.Should().Contain("# InfernalHierarchy");
        md.Should().Contain("## Tools");
    }

    [Fact]
    public async Task Personas_List_Load_Validate_Save_Works()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // list
        var listRes = await client.GetAsync("/api/personas");
        listRes.EnsureSuccessStatusCode();

        var listText = await listRes.Content.ReadAsStringAsync();
        listText.Should().Contain("testdemon");

        // load
        var getRes = await client.GetAsync("/api/personas/testdemon");
        getRes.EnsureSuccessStatusCode();

        var payload = await getRes.Content.ReadFromJsonAsync<PersonaGetResponse>();
        payload.Should().NotBeNull();
        payload!.json.Should().Contain("\"name\"");

        // validate (ok)
        var validateRes = await client.PostAsJsonAsync("/api/personas/testdemon/validate", new { json = payload.json });
        validateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // save (change title)
        var updatedJson = payload.json.Replace("\"Test Demon\"", "\"Test Demon Updated\"");
        var saveRes = await client.PutAsJsonAsync("/api/personas/testdemon", new { json = updatedJson });
        saveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes2 = await client.GetAsync("/api/personas/testdemon");
        var text2 = await getRes2.Content.ReadAsStringAsync();
        text2.Should().Contain("Test Demon Updated");
    }

    private sealed record PersonaGetResponse(string name, string json);
}
