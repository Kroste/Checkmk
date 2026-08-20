using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der Ausfall-Cache ist der Grund, warum das Verlagern der globalen
/// Einstellungen auf FOC-SQL01 die Verfuegbarkeit nicht verschlechtert. Ohne
/// Tests wuerde dieser Pfad nie geprueft — man braeuchte dafuer einen
/// Serverausfall.
/// </summary>
public class GlobalSettingsCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cockpit-cache-tests", Guid.NewGuid().ToString("N"));

    private string PathIn(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* Aufraeumen ist Kuer */ }
    }

    [Fact]
    public void Write_then_read_round_trips()
    {
        var cache = new GlobalSettingsCache(PathIn("globals.json"));
        var rows = new Dictionary<string, string?>
        {
            ["HostDefaultDomain"] = "lhp.intern",
            ["ShowHostCreation"] = "False",
            ["Leer"] = null
        };

        cache.Write(rows);

        cache.Read().Should().BeEquivalentTo(rows);
    }

    [Fact]
    public void Write_creates_missing_directory()
    {
        // Erste Ausfuehrung auf einem frischen Rechner: der Ordner existiert nicht.
        var cache = new GlobalSettingsCache(PathIn(Path.Combine("tief", "drin", "globals.json")));

        cache.Write(new Dictionary<string, string?> { ["A"] = "1" });

        cache.Read().Should().ContainKey("A");
    }

    [Fact]
    public void Missing_file_reads_as_null()
    {
        new GlobalSettingsCache(PathIn("gibtsnicht.json")).Read().Should().BeNull();
    }

    [Fact]
    public void Corrupt_file_reads_as_null_instead_of_throwing()
    {
        // Halb geschriebene Datei nach einem Absturz. Lieber Vorgaben als ein
        // Stand, dem wir nicht trauen koennen — und auf keinen Fall eine
        // Exception beim Start.
        var path = PathIn("kaputt.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ \"HostDefaultDomain\": \"lhp.int");

        new GlobalSettingsCache(path).Read().Should().BeNull();
    }

    [Fact]
    public void Empty_file_reads_as_null()
    {
        var path = PathIn("leer.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "");

        new GlobalSettingsCache(path).Read().Should().BeNull();
    }

    [Fact]
    public void Empty_object_reads_as_null_so_defaults_win()
    {
        var path = PathIn("leeresobjekt.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{}");

        new GlobalSettingsCache(path).Read().Should().BeNull();
    }

    [Fact]
    public void Unwritable_path_does_not_throw()
    {
        // Ein nicht schreibbarer Cache darf den frisch gelesenen Stand nicht
        // kippen — Write schluckt deshalb bewusst.
        var cache = new GlobalSettingsCache(PathIn("ordner-als-datei"));
        Directory.CreateDirectory(cache.Path);   // Pfad ist ein Verzeichnis

        var act = () => cache.Write(new Dictionary<string, string?> { ["A"] = "1" });

        act.Should().NotThrow();
    }
}
