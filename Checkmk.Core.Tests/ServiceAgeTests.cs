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

    // --- svc_check_age (Viewer-Spalte "Letzter Check") -------------------

    [Fact]
    public void CheckAge_is_dash_when_the_service_was_never_checked()
        => new ServiceStatus { LastCheckUnix = 0 }.CheckAge.Should().Be("-");

    [Fact]
    public void CheckAge_is_independent_of_the_state_age()
    {
        var svc = new ServiceStatus
        {
            LastCheckUnix = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds(),
            LastStateChangeUnix = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds()
        };

        svc.CheckAge.Should().EndWith("m");
        svc.Age.Should().Contain("d");
    }

    // --- service_display_name -------------------------------------------

    [Fact]
    public void DisplayName_falls_back_to_description_when_the_site_sends_none()
        => new ServiceStatus { Description = "CPU load" }
            .DisplayNameOrDescription.Should().Be("CPU load");

    [Fact]
    public void DisplayName_wins_when_the_site_sends_a_service_alias()
        => new ServiceStatus { Description = "CPU load", DisplayName = "Prozessorlast" }
            .DisplayNameOrDescription.Should().Be("Prozessorlast");
}
