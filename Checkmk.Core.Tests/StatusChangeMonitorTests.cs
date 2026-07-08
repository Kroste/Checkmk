using Checkmk.App.Services;
using Checkmk.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class StatusChangeMonitorTests
{
    private static ServiceStatus Svc(string host, string desc, ServiceState state)
        => new() { HostName = host, Description = desc, State = (int)state };

    [Fact]
    public void First_diff_only_initializes_without_changes()
    {
        var m = new StatusChangeMonitor();
        var s = m.Diff([Svc("h1", "cpu", ServiceState.Ok), Svc("h1", "mem", ServiceState.Critical)]);
        s.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Ok_to_critical_counts_as_new_problem()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "cpu", ServiceState.Ok), Svc("h1", "mem", ServiceState.Ok)]);
        var s = m.Diff([Svc("h1", "cpu", ServiceState.Critical), Svc("h1", "mem", ServiceState.Ok)]);

        s.NewProblems.Should().Be(1);
        s.Recoveries.Should().Be(0);
        s.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Recovery_and_severity_change_are_classified_separately()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Critical), Svc("h1", "b", ServiceState.Warning)]);
        var s = m.Diff([Svc("h1", "a", ServiceState.Ok), Svc("h1", "b", ServiceState.Critical)]);

        s.Recoveries.Should().Be(1);    // a: CRIT -> OK
        s.OtherChanges.Should().Be(1);  // b: WARN -> CRIT
        s.NewProblems.Should().Be(0);
    }

    [Fact]
    public void Reset_prevents_false_alarm_on_the_following_diff()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Ok)]);
        m.Reset();
        var s = m.Diff([Svc("h1", "a", ServiceState.Critical)]);
        s.HasChanges.Should().BeFalse(); // erster Lauf nach Reset initialisiert nur
    }
}
