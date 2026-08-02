using System.Net;
using FluentAssertions;
using InfernalHierarchy.Host.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class OperationalAuthGuardTests
{
    [Fact]
    public void ForbidIfUnauthorized_LocalOnlyLoopback_ShouldAllowWithoutApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;

        var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, localOnly: true, configuredApiKey: "");

        forbid.Should().BeNull();
    }

    [Fact]
    public void ForbidIfUnauthorized_NonLocalWithoutApiKey_ShouldReturnServiceUnavailable()
    {
        var ctx = new DefaultHttpContext();

        var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, localOnly: false, configuredApiKey: "");

        forbid.Should().NotBeNull();
        var status = forbid.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void ForbidIfUnauthorized_NonLocalWithInvalidHeader_ShouldReturnUnauthorized()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[OperationalAuthGuard.HeaderName] = "wrong";

        var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, localOnly: false, configuredApiKey: "secret");

        forbid.Should().NotBeNull();
        var status = forbid.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void ForbidIfUnauthorized_NonLocalWithValidHeader_ShouldAllow()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[OperationalAuthGuard.HeaderName] = "secret";

        var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, localOnly: false, configuredApiKey: "secret");

        forbid.Should().BeNull();
    }
}
