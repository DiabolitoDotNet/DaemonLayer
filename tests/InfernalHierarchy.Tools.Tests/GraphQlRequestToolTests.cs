using System.Net;
using System.Text;
using FluentAssertions;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.GraphQL;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class GraphQlRequestToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldRejectMutation_WhenReadOnlyIsRequired()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var tool = new GraphQlRequestTool(
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GraphQlToolOptions
            {
                Enabled = true,
                RequireReadOnly = true,
                AllowedHosts = new List<string> { "example.com" }
            }));

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["endpoint"] = "https://example.com/graphql",
            ["query"] = "mutation { doThing }"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("read-only");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSendQueryAndReturnPayload()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"hello\":\"world\"}}", Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object, disposeHandler: false);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(x => x.CreateClient(nameof(GraphQlRequestTool))).Returns(httpClient);

        var tool = new GraphQlRequestTool(
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GraphQlToolOptions
            {
                Enabled = true,
                RequireReadOnly = true,
                AllowedHosts = new List<string> { "example.com" }
            }));

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["endpoint"] = "https://example.com/graphql",
            ["query"] = "query { hello }"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("hello");
        result.Metadata["status"].Should().Be(200);
    }
}