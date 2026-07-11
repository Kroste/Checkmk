using System.Text.Json;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Zuordnung Hostname -> Domain, zentral auf dem Fileshare.</summary>
public sealed class HostDomainEntry
{
    public string Host { get; set; } = "";
    public string Domain { get; set; } = "";
}

public sealed class HostDomainState
{
    public List<HostDomainEntry> Hosts { get; set; } = new();
}

public interface IHostDomainStore
{
    HostDomainState Load();
    void Save(HostDomainState state);
    string FilePath { get; }
}

/// <summary>
/// Zentral auf dem Fileshare abgelegte Zuordnung Hostname -> Domain.
/// Datei: <c>\\Samba01\...\hosts.json</c> (Pfad ueber Bootstrap ueberschreibbar).
/// Bewusst unverschluesselt: sind keine Secrets, sondern Metadaten, die alle
/// Cockpit-Nutzer teilen sollen.
/// </summary>
public sealed class HostDomainStore : IHostDomainStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _path;

    public string FilePath => _path;

    public HostDomainStore()
    {
        _path = Bootstrap.LoadOrCreate().SharedHostsPath;
        var dir = Path.GetDirectoryName(_path);
        try
        {
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Hosts-Zielverzeichnis konnte nicht erstellt werden: {Path}", dir);
        }
    }

    public HostDomainState Load()
    {
        if (!File.Exists(_path)) return new HostDomainState();
        try
        {
            return JsonSerializer.Deserialize<HostDomainState>(File.ReadAllText(_path))
                   ?? new HostDomainState();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "hosts.json konnte nicht gelesen werden: {Path}", _path);
            return new HostDomainState();
        }
    }

    public void Save(HostDomainState state)
    {
        try
        {
            File.WriteAllText(_path,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            Log.Info("hosts.json gespeichert nach {Path}", _path);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "hosts.json konnte nicht gespeichert werden: {Path}", _path);
        }
    }
}
