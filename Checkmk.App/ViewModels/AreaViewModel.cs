using System.Collections.ObjectModel;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using Checkmk.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

/// <summary>
/// Bereichsbaum mit Status-Rollup. Bewusst <b>vor</b> der Karte gebaut: Der
/// Nutzen steckt im Rollup („welcher Standort hat gerade ein Problem"), die
/// Karte ist die Hülle darum. Wer Bereiche hier pflegt, kann sie später
/// zeichnen, ohne die Zuordnung noch einmal anzufassen.
/// </summary>
public sealed partial class AreaViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAreaStore _areas;
    private readonly StatusViewModel _status;

    /// <summary>Knoten je Bereichs-Id — damit der Refresh die Aggregate in place
    /// setzen kann, statt den Baum neu zu bauen.</summary>
    private readonly Dictionary<int, AreaNodeViewModel> _byId = [];

    /// <summary>Signatur des zuletzt gebauten Baums (Id + Name + Elternteil).
    /// Ändert sie sich nicht, bleibt der Baum stehen und behält seinen
    /// Aufklapp-Zustand.</summary>
    private string _builtFrom = "";

    private readonly AreaNodeViewModel _unassigned =
        new(AreaNodeViewModel.UnassignedId, "Ohne Bereich", isUnassigned: true);

    public ObservableCollection<AreaNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanEditSelection))]
    private AreaNodeViewModel? _selectedNode;

    public bool HasSelection => SelectedNode is not null;

    /// <summary>Der Sammelknoten „Ohne Bereich" ist kein Datensatz — umbenennen
    /// und löschen gehen dort nicht.</summary>
    public bool CanEditSelection => SelectedNode is { IsUnassigned: false };

    /// <summary>false im Viewer-Modus.</summary>
    public bool CanWrite { get; }

    /// <summary>Hostnamen ohne Bereich im aktuellen Filter — die Arbeitsliste
    /// beim Zuordnen.</summary>
    public IReadOnlyList<string> UnassignedHosts { get; private set; } = [];

    public AreaViewModel(IAreaStore areas, StatusViewModel status, ViewerMode viewer)
    {
        _areas = areas;
        _status = status;
        CanWrite = viewer.CanWrite;

        // Der Status-Tab liefert die Hosts, die auf den aktiven Filter passen —
        // genau die Linse, die den Rollup ausmacht.
        _status.Refreshed += (services, _) => Recompute(services);
    }

    /// <summary>Baum aus der Datenbank holen und mit dem letzten Statusstand füllen.</summary>
    public async Task InitializeAsync()
    {
        await _areas.RefreshAsync();
        Recompute(_status.AllServices);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        try
        {
            IsBusy = true;
            await _areas.RefreshAsync();
            Recompute(_status.AllServices);
            StatusMessage = $"{_byId.Count} Bereiche geladen.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Baut den Baum (falls nötig) und setzt die Aggregate. Läuft nach jedem
    /// Status-Refresh, also alle paar Sekunden — deshalb erst die günstige
    /// Signaturprüfung, bevor irgendetwas neu entsteht.
    /// </summary>
    public void Recompute(IReadOnlyList<ServiceStatus> services)
    {
        var snapshot = _areas.Current;
        RebuildIfChanged(snapshot);

        var worstPerHost = AreaRollup.WorstStatePerHost(services);
        var aggregates = AreaRollup.Compute(snapshot.Areas, snapshot.HostToArea, worstPerHost);

        foreach (var (areaId, node) in _byId)
            node.Apply(aggregates.GetValueOrDefault(areaId, AreaAggregate.Empty));

        ApplyUnassigned(snapshot, worstPerHost);
    }

    /// <summary>
    /// Hosts im Filter, die keinem Bereich zugeordnet sind. Ohne diese Anzeige
    /// wäre bei 1105 Hosts nicht erkennbar, wie weit die Zuordnung gediehen ist —
    /// und ein vergessener Host fiele niemandem auf, weil er schlicht nirgends
    /// auftaucht.
    /// </summary>
    private void ApplyUnassigned(AreaSnapshot snapshot,
        IReadOnlyDictionary<string, ServiceState> worstPerHost)
    {
        var known = snapshot.Areas.Select(a => a.AreaId).ToHashSet();

        var hosts = new List<string>();
        var problems = 0;
        var worst = ServiceState.Ok;

        foreach (var (host, state) in worstPerHost)
        {
            // Eine Zuordnung auf einen Bereich, den es nicht mehr gibt, zählt
            // ebenfalls als „ohne Bereich" — sonst verschwände der Host ganz.
            if (snapshot.HostToArea.TryGetValue(host, out var areaId) && known.Contains(areaId))
                continue;

            hosts.Add(host);
            if (state != ServiceState.Ok) problems++;
            if (Rank(state) > Rank(worst)) worst = state;
        }

        UnassignedHosts = hosts;
        _unassigned.Apply(new AreaAggregate(hosts.Count, problems, worst, hosts.Count > 0));

        static int Rank(ServiceState s) => s switch
        {
            ServiceState.Critical => 3,
            ServiceState.Warning => 2,
            ServiceState.Unknown => 1,
            _ => 0
        };
    }

    private void RebuildIfChanged(AreaSnapshot snapshot)
    {
        var signature = string.Join('|', snapshot.Areas
            .OrderBy(a => a.AreaId)
            .Select(a => $"{a.AreaId}:{a.ParentAreaId}:{a.Name}"));
        if (signature == _builtFrom && Roots.Count > 0) return;
        _builtFrom = signature;

        var selectedId = SelectedNode?.AreaId;

        _byId.Clear();
        Roots.Clear();

        foreach (var a in snapshot.Areas)
            _byId[a.AreaId] = new AreaNodeViewModel(a.AreaId, a.Name);

        foreach (var a in snapshot.Areas.OrderBy(a => a.SortOrder).ThenBy(a => a.Name))
        {
            var node = _byId[a.AreaId];
            if (a.ParentAreaId is { } p && _byId.TryGetValue(p, out var parent))
                parent.Children.Add(node);
            else
                Roots.Add(node);   // auch verwaiste Bereiche bleiben sichtbar
        }

        Roots.Add(_unassigned);

        // Auswahl über die Id nachziehen, sonst springt sie beim Anlegen weg.
        if (selectedId is { } id)
            SelectedNode = id == AreaNodeViewModel.UnassignedId
                ? _unassigned
                : _byId.GetValueOrDefault(id);
    }

    // -----------------------------------------------------------------------
    // Pflege
    // -----------------------------------------------------------------------

    /// <summary>Legt einen Bereich an — unterhalb der Auswahl, sonst als Wurzel.</summary>
    public async Task<bool> CreateAsync(string name, bool asChildOfSelection)
    {
        if (!CanWrite || string.IsNullOrWhiteSpace(name)) return false;

        var parent = asChildOfSelection && SelectedNode is { IsUnassigned: false } s
            ? s.AreaId
            : (int?)null;

        try
        {
            IsBusy = true;
            await _areas.CreateAsync(name, parent);
            Recompute(_status.AllServices);
            StatusMessage = $"Bereich „{name.Trim()}“ angelegt.";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Bereich konnte nicht angelegt werden.");
            StatusMessage = $"Anlegen fehlgeschlagen: {ex.Message}";
            return false;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> RenameAsync(string name)
    {
        if (!CanWrite || SelectedNode is not { IsUnassigned: false } node) return false;
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            IsBusy = true;
            await _areas.RenameAsync(node.AreaId, name);
            Recompute(_status.AllServices);
            StatusMessage = $"Bereich umbenannt in „{name.Trim()}“.";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Bereich konnte nicht umbenannt werden.");
            StatusMessage = $"Umbenennen fehlgeschlagen: {ex.Message}";
            return false;
        }
        finally { IsBusy = false; }
    }

    /// <summary>Löscht den gewählten Bereich. Gibt eine Klartext-Begründung
    /// zurück, wenn er noch nicht leer ist — sonst bliebe es beim wirkungslosen
    /// Klick.</summary>
    public async Task<string?> DeleteSelectedAsync()
    {
        if (!CanWrite || SelectedNode is not { IsUnassigned: false } node) return null;

        try
        {
            IsBusy = true;
            var result = await _areas.DeleteAsync(node.AreaId);
            if (!result.Deleted)
            {
                var parts = new List<string>();
                if (result.ChildCount > 0) parts.Add($"{result.ChildCount} Unterbereich(e)");
                if (result.HostCount > 0) parts.Add($"{result.HostCount} zugeordnete(n) Host(s)");
                return $"„{node.Name}“ enthält noch {string.Join(" und ", parts)} — "
                     + "erst leeren, dann löschen.";
            }

            SelectedNode = null;
            Recompute(_status.AllServices);
            StatusMessage = $"Bereich „{node.Name}“ gelöscht.";
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Bereich konnte nicht geloescht werden.");
            return $"Löschen fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Ordnet Hosts einem Bereich zu (null = Zuordnung entfernen).</summary>
    public async Task AssignAsync(IReadOnlyList<string> hosts, int? areaId)
    {
        if (!CanWrite || hosts.Count == 0) return;

        try
        {
            IsBusy = true;
            await _areas.AssignAsync(hosts, areaId);
            Recompute(_status.AllServices);

            var target = areaId is { } id
                ? _byId.GetValueOrDefault(id)?.Name ?? id.ToString()
                : "(Zuordnung entfernt)";
            StatusMessage = $"{hosts.Count} Host(s) → {target}.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Bereichszuordnung fehlgeschlagen.");
            StatusMessage = $"Zuordnung fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Alle echten Bereiche flach, für Auswahldialoge.</summary>
    public IReadOnlyList<AreaNodeViewModel> AllAreas()
        => [.. Roots.Where(r => !r.IsUnassigned).SelectMany(r => r.Flatten())];
}
