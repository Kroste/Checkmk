using Checkmk.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class ServiceAgeTests
{
    [Fact]
    public void Age_is_dash_when_no_state_change()
        => new ServiceStatus { LastStateChangeUnix = 0 }.Age.Should().Be("-");

    [Fact]
    public void Age_shows_hours_for_a_change_two_hours_ago()
    {
        var svc = new ServiceStatus
        {
            LastStateChangeUnix = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()
        };
        svc.Age.Should().Contain("h");
    }

    [Fact]
    public void Age_shows_minutes_for_a_recent_change()
    {
        var svc = new ServiceStatus
        {
            LastStateChangeUnix = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds()
        };
        svc.Age.Should().EndWith("m");
    }
}
