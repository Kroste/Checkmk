using System.Text.Json;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Persistierbare UI-Praeferenzen fuer den Status-Tab. Getrennt vom Filter-Store,
/// weil semantisch anders (Ansichts-Zustand vs. Favoriten-Bibliothek).
/// </summary>
public sealed class StatusViewState
{
    public bool TreeView { get; set; }
    public string FilterText { get; set; } = "";
    public bool OnlyProblems { get; set; } = true;
    public bool OnlyOpen { get; set; }
    public bool AutoRefresh { get; set; }
    public int RefreshSeconds { get; set; } = 30;
}

public interface IStatusViewStateStore
{
    StatusViewState Load();
    void Save(StatusViewState state);
}

/// <summary>
/// Persistiert unter <c>%APPDATA%\Kroste\Checkmk\statusview.json</c>. User-lokal,
/// unverschluesselt — reine UI-Praeferenzen, keine sensiblen Daten.
/// </summary>
public sealed class StatusViewStateStore : IStatusViewStateStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _path;

    public StatusViewStateStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "statusview.json");
    }

    public StatusViewState Load()
    {
        if (!File.Exists(_path))
            return new StatusViewState();
        try
        {
            return JsonSerializer.Deserialize<StatusViewState>(File.ReadAllText(_path))
                   ?? new StatusViewState();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "statusview.json konnte nicht gelesen werden — nutze Defaults.");
            return new StatusViewState();
        }
    }

    public void Save(StatusViewState state)
    {
        try
        {
            File.WriteAllText(_path,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "statusview.json konnte nicht gespeichert werden.");
        }
    }
}
