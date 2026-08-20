using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Die globalen Vorgaben kommen aus einer Tabelle, die mehrere Leute pflegen —
/// halb gefuellt oder mit einem Tippfehler ist der Normalfall, nicht die
/// Ausnahme. Nichts davon darf den Start verhindern.
/// </summary>
public class CockpitGlobalsTests
{
    [Fact]
    public void Missing_keys_fall_back_to_defaults()
    {
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>());

        var defaults = new CockpitGlobals();
        globals.HostDefaultDomain.Should().Be(defaults.HostDefaultDomain);
        globals.UpdateChannelUrl.Should().Be(defaults.UpdateChannelUrl);
        globals.HostOsAttributeKeys.Should().BeEquivalentTo(defaults.HostOsAttributeKeys);
        globals.ShowHostCreation.Should().BeFalse();
    }

    [Fact]
    public void Values_from_rows_win_over_defaults()
    {
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyHostDefaultDomain] = "potsdam.intern",
            [CockpitGlobals.KeyShowHostCreation] = "true",
            [CockpitGlobals.KeyHostOsAttributeKeys] = """["standort_os","os_family"]"""
        });

        globals.HostDefaultDomain.Should().Be("potsdam.intern");
        globals.ShowHostCreation.Should().BeTrue();
        globals.HostOsAttributeKeys.Should().Equal("standort_os", "os_family");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_values_do_not_override_defaults(string? value)
    {
        // Eine leer gelassene Zeile ist kein "setze auf leer" — sonst haette
        // ein versehentlich geleertes Feld die Default-Domain geloescht.
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyHostDefaultDomain] = value
        });

        globals.HostDefaultDomain.Should().Be(new CockpitGlobals().HostDefaultDomain);
    }

    [Fact]
    public void Broken_json_list_falls_back_instead_of_throwing()
    {
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyHostOsAttributeKeys] = "[\"kaputt\", "
        });

        globals.HostOsAttributeKeys.Should().BeEquivalentTo(new CockpitGlobals().HostOsAttributeKeys);
    }

    [Fact]
    public void Empty_json_list_falls_back_to_defaults()
    {
        // Eine leere Kandidatenliste hiesse: OS-Familie wird nie erkannt.
        // Das ist nie gewollt, also gilt sie als "nicht gesetzt".
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyHostOsAttributeKeys] = "[]"
        });

        globals.HostOsAttributeKeys.Should().BeEquivalentTo(new CockpitGlobals().HostOsAttributeKeys);
    }

    [Fact]
    public void Unparseable_bool_falls_back()
    {
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyShowHostCreation] = "ja"
        });

        globals.ShowHostCreation.Should().BeFalse();
    }

    [Fact]
    public void Round_trip_through_rows_preserves_everything()
    {
        var original = new CockpitGlobals
        {
            HostDefaultDomain = "svp.lan",
            UpdateChannelUrl = "https://example.invalid/releases",
            HostOsAttributeKeys = ["a", "b", "c"],
            ShowHostCreation = true
        };

        var restored = CockpitGlobals.FromRows(
            original.ToRows().ToDictionary(x => x.Key, x => x.Value));

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Rows_are_matched_case_insensitively_like_the_database()
    {
        // Der Provider baut sein Dictionary mit OrdinalIgnoreCase, weil SQL
        // Server standardmaessig case-insensitive vergleicht. Wer den Schluessel
        // in der Tabelle klein schreibt, soll nicht ins Leere laufen.
        var rows = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["hostdefaultdomain"] = "klein.intern"
        };

        CockpitGlobals.FromRows(rows).HostDefaultDomain.Should().Be("klein.intern");
    }
}
