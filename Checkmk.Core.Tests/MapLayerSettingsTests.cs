using System.Text.Json;
using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Die Liste der Kartenhintergründe kommt aus einer Tabelle, die von Hand
/// gepflegt wird. Ein Tippfehler darf die Karte höchstens auf die Vorgabe
/// zurückfallen lassen, nicht den Start verhindern.
/// </summary>
public class MapLayerSettingsTests
{
    [Fact]
    public void Defaults_offer_a_non_satellite_option()
    {
        // Der Grund für den Umschalter: auf einem Luftbild sind eingefärbte
        // Flächen schwer zu lesen. Mindestens ein Kartenhintergrund ohne Foto
        // muss also dabei sein.
        var layers = new CockpitGlobals().MapLayers;

        layers.Should().HaveCountGreaterThan(1);
        layers.Should().Contain(l => l.Name == "Stadtplan");
        layers.Should().Contain(l => l.Name.Contains("Topographisch"));
        layers.Should().AllSatisfy(l =>
        {
            l.Url.Should().StartWith("https://");
            l.Layer.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void Layers_round_trip_through_the_settings_rows()
    {
        var original = new CockpitGlobals
        {
            MapLayers =
            [
                new MapLayerDefinition("Werk", "https://example.invalid/wms", "werk_raster")
            ]
        };

        var restored = CockpitGlobals.FromRows(
            original.ToRows().ToDictionary(x => x.Key, x => x.Value));

        restored.MapLayers.Should().ContainSingle()
            .Which.Should().Be(new MapLayerDefinition("Werk", "https://example.invalid/wms", "werk_raster"));
    }

    [Fact]
    public void Broken_json_falls_back_to_the_defaults()
    {
        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyMapLayers] = "[{\"Name\":\"kaputt\", "
        });

        globals.MapLayers.Should().BeEquivalentTo(new CockpitGlobals().MapLayers);
    }

    [Fact]
    public void Entries_without_url_or_layer_are_dropped()
    {
        // Ein halb ausgefuellter Eintrag waere eine stumme Fehlkachel — die
        // Karte bliebe grau und niemand wuesste warum.
        var json = JsonSerializer.Serialize(new[]
        {
            new MapLayerDefinition("gut", "https://example.invalid/wms", "l1"),
            new MapLayerDefinition("ohne Layer", "https://example.invalid/wms", "  "),
            new MapLayerDefinition("", "https://example.invalid/wms", "l2")
        });

        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyMapLayers] = json
        });

        globals.MapLayers.Should().ContainSingle().Which.Name.Should().Be("gut");
    }

    [Fact]
    public void An_entirely_unusable_list_falls_back_instead_of_leaving_no_map()
    {
        var json = JsonSerializer.Serialize(new[] { new MapLayerDefinition("x", "", "") });

        var globals = CockpitGlobals.FromRows(new Dictionary<string, string?>
        {
            [CockpitGlobals.KeyMapLayers] = json
        });

        globals.MapLayers.Should().BeEquivalentTo(new CockpitGlobals().MapLayers);
    }
}
