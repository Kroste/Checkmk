using Checkmk.Core;
using Checkmk.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class CheckmkOptionsTests
{
    [Fact]
    public void BaseUri_uses_v1_path_and_https_by_default()
    {
        var opts = new CheckmkOptions
        {
            Host = "monitoring.lhp.intern", Site = "prod",
            Username = "automation", Secret = "x"
        };

        opts.BaseUri.ToString()
            .Should().Be("https://monitoring.lhp.intern/prod/check_mk/api/v1/");
    }

    [Fact]
    public void BaseUri_honours_http_and_custom_api_version()
    {
        var opts = new CheckmkOptions
        {
            Host = "cmk", Site = "test", Username = "a", Secret = "x",
            UseHttps = false, ApiVersion = "unstable"
        };

        opts.BaseUri.ToString().Should().Be("http://cmk/test/check_mk/api/unstable/");
    }
}

public class StateMappingTests
{
    [Theory]
    [InlineData(0, ServiceState.Ok)]
    [InlineData(1, ServiceState.Warning)]
    [InlineData(2, ServiceState.Critical)]
    [InlineData(3, ServiceState.Unknown)]
    public void Service_state_maps_from_livestatus_code(int code, ServiceState expected)
        => new ServiceStatus { State = code }.ServiceState.Should().Be(expected);

    [Theory]
    [InlineData(0, HostState.Up)]
    [InlineData(1, HostState.Down)]
    [InlineData(2, HostState.Unreachable)]
    public void Host_state_maps_from_livestatus_code(int code, HostState expected)
        => new HostStatus { State = code }.HostState.Should().Be(expected);

    [Fact]
    public void Acknowledged_and_downtime_flags_derive_correctly()
    {
        var svc = new ServiceStatus { Acknowledged = 1, ScheduledDowntimeDepth = 2 };
        svc.IsAcknowledged.Should().BeTrue();
        svc.InDowntime.Should().BeTrue();
    }
}
