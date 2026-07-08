using System.Collections.ObjectModel;
using Checkmk.App.Services;
using Checkmk.Core.Models;

namespace Checkmk.App.ViewModels;

/// <summary>Oberster Baum-Knoten: ein Host mit OS-Pictogram, Problem-Zaehler und seinen Services.</summary>
public sealed class HostNodeViewModel(
    string hostName, OsFamily os, IEnumerable<ServiceStatus> services)
{
    public string HostName { get; } = hostName;
    public OsFamily Os { get; } = os;
    public string OsLabel => OsDetection.Label(Os);

    public ObservableCollection<ServiceStatus> Services { get; } = [.. services];

    public int ProblemCount => Services.Count(s => s.ServiceState != ServiceState.Ok);
    public bool HasProblems => ProblemCount > 0;
    public string ProblemBadge => $"({ProblemCount})";

    /// <summary>Schlechtester Status unter den Services — fuer den Ampelpunkt des Host-Knotens.
    /// Prioritaet: CRIT &gt; WARN &gt; UNKNOWN &gt; OK.</summary>
    public ServiceState WorstState =>
        Services.Any(s => s.ServiceState == ServiceState.Critical) ? ServiceState.Critical
        : Services.Any(s => s.ServiceState == ServiceState.Warning) ? ServiceState.Warning
        : Services.Any(s => s.ServiceState == ServiceState.Unknown) ? ServiceState.Unknown
        : ServiceState.Ok;
}
