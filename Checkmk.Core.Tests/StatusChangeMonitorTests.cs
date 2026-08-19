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

    // --- WorstNewProblem / HasWorsened (Viewer-Popup) --------------------

    /// <summary>Reine Recoveries duerfen im Viewer-Modus kein Fenster hochreissen.</summary>
    [Fact]
    public void Pure_recovery_does_not_count_as_worsened()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Critical)]);
        var s = m.Diff([Svc("h1", "a", ServiceState.Ok)]);

        s.HasChanges.Should().BeTrue();
        s.HasWorsened.Should().BeFalse();
        s.WorstNewProblem.Should().BeNull();
    }

    [Fact]
    public void New_problem_is_reported_with_the_affected_service()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "cpu", ServiceState.Ok)]);
        var s = m.Diff([Svc("h1", "cpu", ServiceState.Warning)]);

        s.HasWorsened.Should().BeTrue();
        s.WorstNewProblem!.HostName.Should().Be("h1");
        s.WorstNewProblem.Description.Should().Be("cpu");
    }

    /// <summary>Der Viewer soll auf das Schlimmste springen, nicht auf das Erstbeste.</summary>
    [Fact]
    public void Critical_wins_over_warning_as_the_spotlight_target()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Ok), Svc("h1", "b", ServiceState.Ok)]);
        var s = m.Diff([Svc("h1", "a", ServiceState.Warning), Svc("h1", "b", ServiceState.Critical)]);

        s.NewProblems.Should().Be(2);
        s.WorstNewProblem!.Description.Should().Be("b");
    }

    [Fact]
    public void Worsening_from_warning_to_critical_also_sets_the_target()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Warning)]);
        var s = m.Diff([Svc("h1", "a", ServiceState.Critical)]);

        s.OtherChanges.Should().Be(1);
        s.HasWorsened.Should().BeTrue();
        s.WorstNewProblem!.Description.Should().Be("a");
    }

    /// <summary>Gleichzeitig Recovery und neues Problem: das Problem gewinnt.</summary>
    [Fact]
    public void Recovery_alongside_a_new_problem_still_yields_a_target()
    {
        var m = new StatusChangeMonitor();
        m.Diff([Svc("h1", "a", ServiceState.Critical), Svc("h1", "b", ServiceState.Ok)]);
        var s = m.Diff([Svc("h1", "a", ServiceState.Ok), Svc("h1", "b", ServiceState.Critical)]);

        s.Recoveries.Should().Be(1);
        s.HasWorsened.Should().BeTrue();
        s.WorstNewProblem!.Description.Should().Be("b");
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
