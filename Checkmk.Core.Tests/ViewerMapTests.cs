using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Die Kiosk-Karte im Viewer-Profil (Roadmap 28).
///
/// Gedacht für den Bildschirm im Leitstand oder beim Wachschutz: eine
/// Stadtkarte, auf der ein Standort grün, gelb oder rot ist. Welche Hosts dabei
/// zählen, entscheidet weiterhin allein der Filter aus <c>view</c>.
/// </summary>
public class ViewerMapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cockpit-viewermap-tests-" + Guid.NewGuid().ToString("N"));

    public ViewerMapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Best-effort */ }
        GC.SuppressFinalize(this);
    }

    private ViewerProfile Load(string json)
    {
        var path = Path.Combine(_dir, ViewerProfile.FileName);
        File.WriteAllText(path, json);
        return ViewerProfile.LoadFrom(path)!;
    }

    private const string MinimalConnection = """
        "connection": {
          "host": "cmk.lhp.intern", "site": "LHP",
          "username": "viewer", "secret": "geheim"
        }
        """;

    [Fact]
    public void Without_a_map_section_the_areas_tab_stays_gone()
    {
        // Wichtig fuer bestehende Ausgaben: Ein Update darf keinem Kiosk
        // ungefragt einen neuen Tab dazustellen.
        var profile = Load($$"""{ {{MinimalConnection}} }""");

        profile.Map.Show.Should().BeFalse();
        new ViewerMode(profile).Map.Should().BeNull();
    }

    [Fact]
    public void Show_false_is_the_same_as_no_section()
    {
        var profile = Load($$"""
        {
          {{MinimalConnection}},
          "map": { "show": false, "area": "Stadthaus" }
        }
        """);

        new ViewerMode(profile).Map.Should().BeNull();
    }

    [Fact]
    public void A_full_map_section_is_read()
    {
        var profile = Load($$"""
        {
          {{MinimalConnection}},
          "map": {
            "show": true,
            "area": "Stadthaus",
            "zoom": 16.5,
            "layer": "Topographisch grau",
            "tree": false
          }
        }
        """);

        var map = new ViewerMode(profile).Map!;
        map.Show.Should().BeTrue();
        map.Area.Should().Be("Stadthaus");
        map.Zoom.Should().Be(16.5);
        map.Layer.Should().Be("Topographisch grau");
        map.Tree.Should().BeFalse();
    }

    [Fact]
    public void The_tree_is_shown_unless_it_is_switched_off()
    {
        // Der Baum ist die Vorgabe: Eine Karte ohne Liste daneben ist die
        // Ausnahme (reine Kartenwand), nicht der Normalfall.
        var profile = Load($$"""
        {
          {{MinimalConnection}},
          "map": { "show": true }
        }
        """);

        var map = new ViewerMode(profile).Map!;
        map.Tree.Should().BeTrue();
        map.Area.Should().BeNull();
        map.Zoom.Should().Be(0);        // 0 = automatisch einpassen
    }

    [Fact]
    public void The_map_section_does_not_unlock_anything_writable()
    {
        // Der Kiosk-Blick auf Bereiche ist lesend. Saemtliche Schreibknoepfe
        // haengen an CanWrite, und das bleibt im Viewer-Modus false.
        var profile = Load($$"""
        {
          {{MinimalConnection}},
          "map": { "show": true, "area": "Stadthaus" }
        }
        """);

        var mode = new ViewerMode(profile);
        mode.IsActive.Should().BeTrue();
        mode.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void A_broken_profile_does_not_fall_back_to_the_full_ui()
    {
        // Dieselbe Regel wie beim uebrigen Viewer-Profil: Ein Tippfehler im
        // JSON darf keinem Nur-Gucker die volle Oberflaeche freischalten.
        var path = Path.Combine(_dir, ViewerProfile.FileName);
        File.WriteAllText(path, "{ das ist kein JSON");

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.Should().NotBeNull();
        profile.LoadError.Should().NotBeNullOrEmpty();
        new ViewerMode(profile).CanWrite.Should().BeFalse();
        // Und ohne lesbares Profil auch keine Karte — lieber der bekannte
        // Status-Tab als eine halb angewandte Vorgabe.
        new ViewerMode(profile).Map.Should().BeNull();
    }
}
