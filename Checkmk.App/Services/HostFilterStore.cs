using System.Text.Json;
using Checkmk.App.Models;
using NLog;

namespace Checkmk.App.Services;

public sealed class HostFilterState
{
    public List<HostFilter> Filters { get; set; } = new();
    public string? ActiveFilterName { get; set; }
}

public interface IHostFilterStore
{
    HostFilterState Load();
    void Save(HostFilterState state);
    string FilePath { get; }
}

/// <summary>
/// Host-Filter liegen user-lokal (nicht sensibel, deshalb unverschluesselt) unter
/// <c>%APPDATA%\Kroste\Checkmk\filter.json</c> bzw. <c>~/.config/Kroste/Checkmk/filter.json</c>.
/// Bewusst nicht auf dem Windows-Fileshare — jeder Nutzer waehlt seine eigenen Favoriten.
/// </summary>
public sealed class HostFilterStore : IHostFilterStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _path;

    public string FilePath => _path;

    public HostFilterStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "filter.json");
    }

    public HostFilterState Load()
    {
        if (!File.Exists(_path))
            return new HostFilterState();

        try
        {
            return JsonSerializer.Deserialize<HostFilterState>(File.ReadAllText(_path))
                   ?? new HostFilterState();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Filter konnten nicht geladen werden — nutze leere Liste.");
            return new HostFilterState();
        }
    }

    public void Save(HostFilterState state)
    {
        try
        {
            File.WriteAllText(_path,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Filter konnten nicht gespeichert werden: {Path}", _path);
        }
    }
}
