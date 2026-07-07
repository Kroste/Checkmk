using System.Text.Json;
using NLog;

namespace Checkmk.App.Services;

public interface IUpdatePreferences
{
    Version? LoadSkippedVersion();
    void SaveSkippedVersion(Version version);
}

/// <summary>
/// Persistiert die vom Nutzer uebersprungene Update-Version — user-lokal, unverschluesselt.
/// Bewusst nicht im Bootstrap-File (das ist fuer Deployment-Konfiguration), sondern in
/// <c>%APPDATA%\Kroste\Checkmk\updates.json</c> bzw. <c>~/.config/Kroste/Checkmk/updates.json</c>.
/// </summary>
public sealed class UpdatePreferences : IUpdatePreferences
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _path;

    public UpdatePreferences()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "updates.json");
    }

    public Version? LoadSkippedVersion()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_path));
            return state?.SkippedVersion is { } s && Version.TryParse(s, out var v) ? v : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "updates.json konnte nicht gelesen werden.");
            return null;
        }
    }

    public void SaveSkippedVersion(Version version)
    {
        try
        {
            File.WriteAllText(_path,
                JsonSerializer.Serialize(new UpdateState { SkippedVersion = version.ToString() },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "updates.json konnte nicht gespeichert werden: {Path}", _path);
        }
    }

    private sealed class UpdateState
    {
        public string? SkippedVersion { get; set; }
    }
}
