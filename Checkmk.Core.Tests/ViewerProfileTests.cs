using Checkmk.App.Services;
using Checkmk.Core;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class ViewerProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cockpit-viewer-tests-" + Guid.NewGuid().ToString("N"));

    public ViewerProfileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Best-effort */ }
        GC.SuppressFinalize(this);
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, ViewerProfile.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Missing_file_means_normal_mode()
        => ViewerProfile.LoadFrom(Path.Combine(_dir, "gibtsnicht.json")).Should().BeNull();

    [Fact]
    public void Full_profile_is_read_including_columns_and_view()
    {
        var path = Write("""
        {
          "title": "Checkmk — Sicht DB",
          "connection": {
            "host": "monitoring.lhp.intern",
            "site": "LHP-Prod",
            "username": "cockpit_viewer",
            "secret": "ABC123",
            "useHttps": true,
            "authMode": "UserBasic"
          },
          "columns": ["host", "service_description", "svc_state_age"],
          "view": {
            "hostRegex": ".*sql.*",
            "filterName": "DB-Server",
            "onlyProblems": true,
            "refreshSeconds": 90
          }
        }
        """);

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.LoadError.Should().BeNull();
        profile.Title.Should().Be("Checkmk — Sicht DB");
        profile.Connection.Host.Should().Be("monitoring.lhp.intern");
        profile.Connection.Secret.Should().Be("ABC123");
        profile.Connection.AuthMode.Should().Be(CheckmkAuthMode.UserBasic);
        profile.Connection.IsComplete.Should().BeTrue();
        profile.Columns.Should().Equal("host", "service_description", "svc_state_age");
        profile.View.HostRegex.Should().Be(".*sql.*");
        profile.View.FilterName.Should().Be("DB-Server");
        profile.View.RefreshSeconds.Should().Be(90);
        profile.View.HasHostScope.Should().BeTrue();
    }

    [Fact]
    public void Empty_column_list_falls_back_to_defaults()
    {
        var path = Write("""
        { "connection": { "host": "h", "site": "s", "username": "u", "secret": "x" } }
        """);

        ViewerProfile.LoadFrom(path)!.Columns.Should().Equal(ViewerProfile.DefaultColumns);
    }

    [Fact]
    public void Unknown_columns_are_dropped_but_known_ones_survive()
    {
        var path = Write("""
        {
          "connection": { "host": "h", "site": "s", "username": "u", "secret": "x" },
          "columns": ["host", "voellig_ausgedacht", "service_state"]
        }
        """);

        ViewerProfile.LoadFrom(path)!.Columns.Should().Equal("host", "service_state");
    }

    [Fact]
    public void All_columns_unknown_falls_back_to_defaults()
    {
        var path = Write("""
        {
          "connection": { "host": "h", "site": "s", "username": "u", "secret": "x" },
          "columns": ["quatsch", "unfug"]
        }
        """);

        ViewerProfile.LoadFrom(path)!.Columns.Should().Equal(ViewerProfile.DefaultColumns);
    }

    /// <summary>
    /// Der entscheidende Punkt fuer die Verteilung: kaputtes JSON darf den
    /// Viewer-Modus NICHT abschalten, sonst haette ein Nutzer, der nur gucken soll,
    /// nach einem Tippfehler ploetzlich die volle Oberflaeche.
    /// </summary>
    [Fact]
    public void Broken_json_keeps_viewer_mode_active_and_reports_the_error()
    {
        var path = Write("{ das ist kein JSON ");

        var profile = ViewerProfile.LoadFrom(path);

        profile.Should().NotBeNull();
        profile!.LoadError.Should().NotBeNullOrEmpty();
        profile.Connection.IsComplete.Should().BeFalse();
        new ViewerMode(profile).CanWrite.Should().BeFalse();
    }

    // --- secretBase64 ----------------------------------------------------

    private static string B64(string plain)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));

    [Fact]
    public void Base64_secret_is_decoded()
    {
        var path = Write($$"""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u",
            "secretBase64": "{{B64("s3cr3t-üäö")}}"
          }
        }
        """);

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.LoadError.Should().BeNull();
        profile.Connection.ResolvedSecret.Should().Be("s3cr3t-üäö");
        profile.Connection.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Plaintext_secret_still_works()
    {
        var path = Write("""
        { "connection": { "host": "h", "site": "s", "username": "u", "secret": "klartext" } }
        """);

        ViewerProfile.LoadFrom(path)!.Connection.ResolvedSecret.Should().Be("klartext");
    }

    [Fact]
    public void Base64_wins_when_both_are_set()
    {
        var path = Write($$"""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u",
            "secret": "alt", "secretBase64": "{{B64("neu")}}"
          }
        }
        """);

        ViewerProfile.LoadFrom(path)!.Connection.ResolvedSecret.Should().Be("neu");
    }

    [Fact]
    public void Surrounding_whitespace_in_base64_is_tolerated()
    {
        var path = Write($$"""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u",
            "secretBase64": "  {{B64("s3cr3t")}}  "
          }
        }
        """);

        ViewerProfile.LoadFrom(path)!.Connection.ResolvedSecret.Should().Be("s3cr3t");
    }

    [Fact]
    public void Broken_base64_is_reported_with_the_encoding_hint()
    {
        var path = Write("""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u",
            "secretBase64": "!!! kein base64 !!!"
          }
        }
        """);

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.LoadError.Should().Contain("secretBase64");
        profile.LoadError.Should().Contain("ToBase64String");
        profile.Connection.IsComplete.Should().BeFalse();
    }

    /// <summary>
    /// Der wahrscheinlichste Bedienfehler: der Admin paste den Klartext in
    /// secretBase64. Ist der zufaellig gueltiges Base64, dekodiert er zu
    /// Byte-Muell — ohne strikte UTF-8-Pruefung faende das niemand, weil der
    /// Server nur „401 Wrong credentials" sagt.
    /// </summary>
    [Fact]
    public void Plaintext_pasted_into_the_base64_field_is_caught()
    {
        // "geheimes" ist gueltiges Base64, dekodiert aber zu ungueltigem UTF-8.
        var path = Write("""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u",
            "secretBase64": "geheimes"
          }
        }
        """);

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.LoadError.Should().Contain("secretBase64");
        profile.Connection.ResolvedSecret.Should().BeEmpty();
    }

    [Fact]
    public void Incomplete_connection_is_reported_but_stays_viewer_mode()
    {
        var path = Write("""
        { "connection": { "host": "h", "site": "s", "username": "u" } }
        """);

        var profile = ViewerProfile.LoadFrom(path)!;

        profile.LoadError.Should().Contain("unvollstaendig");
        profile.Connection.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated_in_a_hand_edited_file()
    {
        var path = Write("""
        {
          // vom Admin von Hand gepflegt
          "connection": { "host": "h", "site": "s", "username": "u", "secret": "x", },
        }
        """);

        ViewerProfile.LoadFrom(path)!.Connection.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Viewer_mode_without_profile_allows_writing()
    {
        var mode = new ViewerMode(null);
        mode.IsActive.Should().BeFalse();
        mode.CanWrite.Should().BeTrue();
    }
}

public class ViewerConnectionSettingsStoreTests
{
    private static ViewerProfile Profile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "viewer-store-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try { return ViewerProfile.LoadFrom(path)!; }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadSecret_returns_the_decoded_base64_secret()
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("s3cr3t"));
        var store = new ViewerConnectionSettingsStore(Profile($$"""
        {
          "connection": {
            "host": "h", "site": "s", "username": "u", "secretBase64": "{{b64}}"
          }
        }
        """));

        store.LoadSecret(store.Load()).Should().Be("s3cr3t");
        store.IsConfigured(store.Load()).Should().BeTrue();
    }

    [Fact]
    public void Load_maps_the_profile_onto_connection_settings()
    {
        var store = new ViewerConnectionSettingsStore(Profile("""
        {
          "connection": {
            "host": "mon.example", "site": "Prod", "username": "viewer",
            "secret": "s3cr3t", "useHttps": false, "ignoreCertificateErrors": true
          }
        }
        """));

        var settings = store.Load();

        settings.Host.Should().Be("mon.example");
        settings.Site.Should().Be("Prod");
        settings.Username.Should().Be("viewer");
        settings.UseHttps.Should().BeFalse();
        settings.IgnoreCertificateErrors.Should().BeTrue();
        store.LoadSecret(settings).Should().Be("s3cr3t");
        store.IsConfigured(settings).Should().BeTrue();
    }

    /// <summary>Genau eine Site => der Umschalter in der Titelleiste bleibt weg.</summary>
    [Fact]
    public void Only_the_profile_site_is_known()
    {
        var store = new ViewerConnectionSettingsStore(Profile("""
        { "connection": { "host": "h", "site": "Prod", "username": "u", "secret": "x" } }
        """));

        store.Load().KnownSites.Should().ContainSingle().Which.Should().Be("Prod");
    }

    [Fact]
    public void Save_and_site_switch_are_no_ops()
    {
        var profile = Profile("""
        { "connection": { "host": "h", "site": "Prod", "username": "u", "secret": "x" } }
        """);
        var store = new ViewerConnectionSettingsStore(profile);

        store.Save(new ConnectionSettings { Host = "anders" }, "neu");
        store.UpdateActiveSite("Andere-Site");

        store.Load().Host.Should().Be("h");
        store.Load().Site.Should().Be("Prod");
        store.LoadSecret(store.Load()).Should().Be("x");
    }
}
