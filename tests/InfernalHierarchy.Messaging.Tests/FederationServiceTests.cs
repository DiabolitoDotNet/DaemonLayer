using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Messaging.Federation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Messaging.Tests;

public sealed class FederationServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    private static FederationService CreateService(StubHttpMessageHandler handler, string localInstanceId)
    {
        var client = new HttpClient(handler);
        return new FederationService(NullLogger<FederationService>.Instance, client, localInstanceId);
    }

    [Fact]
    public async Task RegisterAndUnregister_TracksInstances()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        var instance = new FederatedInstance
        {
            InstanceId = "i1",
            Name = "n",
            BaseUrl = "http://remote",
            IsActive = true,
        };

        await sut.RegisterInstanceAsync(instance);
        var active = await sut.GetActiveInstancesAsync();
        active.Should().ContainSingle(i => i.InstanceId == "i1");

        await sut.UnregisterInstanceAsync("i1");
        (await sut.GetActiveInstancesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WhenTargetMissing_DoesNotCallHttp()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.SendMessageAsync(new FederatedMessage { TargetInstanceId = "missing" });

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WhenTargetFound_PostsJson()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://remote",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow,
        });

        await sut.SendMessageAsync(new FederatedMessage
        {
            TargetInstanceId = "i1",
            MessageType = FederatedMessageType.Broadcast,
            RequiresResponse = false,
        });

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/api/federation/message");
    }

    [Fact]
    public async Task SendMessageAsync_WhenRequiresResponse_ReadsResponseBody()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(new FederatedMessage { CorrelationId = "c" });
            return response;
        });
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://remote",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow,
        });

        await sut.SendMessageAsync(new FederatedMessage
        {
            TargetInstanceId = "i1",
            MessageType = FederatedMessageType.Heartbeat,
            RequiresResponse = true,
        });

        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendMessageAsync_WhenHttpThrows_MarksInstanceInactive()
    {
        var handler = new ThrowingHandler();
        var client = new HttpClient(handler);
        var sut = new FederationService(NullLogger<FederationService>.Instance, client, "local");

        var instance = new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://remote",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow,
        };

        await sut.RegisterInstanceAsync(instance);

        await sut.SendMessageAsync(new FederatedMessage
        {
            TargetInstanceId = "i1",
            MessageType = FederatedMessageType.Broadcast,
        });

        instance.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_WhenHttpReturnsFailureStatus_MarksInstanceInactive()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateService(handler, localInstanceId: "local");

        var instance = new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://remote",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow,
        };

        await sut.RegisterInstanceAsync(instance);

        await sut.SendMessageAsync(new FederatedMessage
        {
            TargetInstanceId = "i1",
            MessageType = FederatedMessageType.Broadcast,
        });

        instance.IsActive.Should().BeFalse();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }

    private sealed class ExplodingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task BroadcastMessageAsync_SendsToAllExceptLocal()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "local", BaseUrl = "http://l", IsActive = true, LastHeartbeat = DateTime.UtcNow });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i1", BaseUrl = "http://r1", IsActive = true, LastHeartbeat = DateTime.UtcNow });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i2", BaseUrl = "http://r2", IsActive = true, LastHeartbeat = DateTime.UtcNow });

        await sut.BroadcastMessageAsync(new FederatedMessage { SourceInstanceId = "local", MessageType = FederatedMessageType.Broadcast, Payload = new() });

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task DelegateTaskAsync_WhenNoRemoteCapacity_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance
        {
            InstanceId = "local",
            BaseUrl = "http://l",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow,
            CurrentAgentCount = 1,
            MaxAgents = 1,
        });

        var selected = await sut.DelegateTaskAsync(new TaskEntry { Id = "t1", Description = "t" });
        selected.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DelegateTaskAsync_SelectsLowestLoadInstance()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(new FederatedMessage { CorrelationId = "c" });
            return response;
        });
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "local", BaseUrl = "http://l", IsActive = true, LastHeartbeat = DateTime.UtcNow });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i1", BaseUrl = "http://r1", IsActive = true, LastHeartbeat = DateTime.UtcNow, CurrentLoad = 0.9, CurrentAgentCount = 1, MaxAgents = 10 });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i2", BaseUrl = "http://r2", IsActive = true, LastHeartbeat = DateTime.UtcNow, CurrentLoad = 0.1, CurrentAgentCount = 1, MaxAgents = 10 });

        var selected = await sut.DelegateTaskAsync(new TaskEntry { Id = "t1", Description = "t" });
        selected.Should().Be("i2");
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task RequestCrossInstanceCollaborationAsync_ReturnsAggregatedResult()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i1", BaseUrl = "http://r1", IsActive = true, LastHeartbeat = DateTime.UtcNow });

        var request = new CollaborationRequest
        {
            Id = "r1",
            InitiatorAgentId = "lucifer",
            Task = "t",
            ParticipantAgentIds = ["a", "b"],
            MinimumParticipants = 1,
            Timeout = TimeSpan.FromSeconds(5)
        };

        var result = await sut.RequestCrossInstanceCollaborationAsync(request);

        result.Decision.Should().Be("CROSS_INSTANCE_AGGREGATION");
        result.Strategy.Should().Be(request.Strategy);
    }

    [Fact]
    public async Task SyncMemoryAsync_TargetsSpecifiedInstancesOnly()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i1", BaseUrl = "http://r1", IsActive = true, LastHeartbeat = DateTime.UtcNow });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i2", BaseUrl = "http://r2", IsActive = true, LastHeartbeat = DateTime.UtcNow });

        await sut.SyncMemoryAsync([new Fact { Content = "c", Source = "s", Category = "cat" }], targetInstances: ["i2"]);

        handler.Requests.Should().ContainSingle(r => r.RequestUri!.ToString().Contains("r2"));
    }

    [Fact]
    public async Task MonitorInstanceHealthAsync_WhenHeartbeatFails_RemovesStaleInstance()
    {
        var handler = new ExplodingHandler();
        var client = new HttpClient(handler);
        var sut = new FederationService(NullLogger<FederationService>.Instance, client, "local");

        var instance = new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://r1",
            IsActive = true,
            LastHeartbeat = DateTime.UtcNow - TimeSpan.FromMinutes(10)
        };

        await sut.RegisterInstanceAsync(instance);

        await sut.MonitorInstanceHealthAsync(CancellationToken.None);

        (await sut.GetActiveInstancesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task MonitorInstanceHealthAsync_WhenHeartbeatTransportFails_DoesNotThrow_AndMarksFreshInstanceHealthyInTimestampOnly()
    {
        var handler = new ThrowingHandler();
        var client = new HttpClient(handler);
        var sut = new FederationService(NullLogger<FederationService>.Instance, client, "local");

        var originalHeartbeat = DateTime.UtcNow;
        var instance = new FederatedInstance
        {
            InstanceId = "i1",
            BaseUrl = "http://r1",
            IsActive = true,
            LastHeartbeat = originalHeartbeat
        };

        await sut.RegisterInstanceAsync(instance);

        await sut.MonitorInstanceHealthAsync(CancellationToken.None);

        instance.IsActive.Should().BeTrue();
        instance.LastHeartbeat.Should().BeAfter(originalHeartbeat);
    }

    [Fact]
    public async Task SelectInstanceForAgentAsync_SelectsLowestLoadAndThenByAgentCount()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateService(handler, localInstanceId: "local");

        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i1", BaseUrl = "http://r1", IsActive = true, LastHeartbeat = DateTime.UtcNow, CurrentLoad = 0.2, CurrentAgentCount = 5, MaxAgents = 10 });
        await sut.RegisterInstanceAsync(new FederatedInstance { InstanceId = "i2", BaseUrl = "http://r2", IsActive = true, LastHeartbeat = DateTime.UtcNow, CurrentLoad = 0.2, CurrentAgentCount = 1, MaxAgents = 10 });

        var selected = await sut.SelectInstanceForAgentAsync(CancellationToken.None);

        selected.Should().Be("i2");
    }
}
