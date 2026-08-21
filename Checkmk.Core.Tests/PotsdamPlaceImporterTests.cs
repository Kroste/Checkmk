using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der Parser liest die Antwort eines fremden Dienstes — das ist die Stelle,
/// die sich ohne unser Zutun ändern kann. Das Beispiel-JSON ist im Aufbau der
/// echten Antwort des FeatureServers <c>Verwaltung_LH_Potsdam</c> nachgebildet.
/// </summary>
public class PotsdamPlaceImporterTests
{
    private const string Sample = """
    {
      "features": [
        {
          "attributes": {
            "OBJECTID": 1, "GLOBALID": "{AAA}", "BEHOERDE": "Bürgerservicecenter",
            "ADRESSE": "Friedrich-Ebert-Str. 79/81", "PLZ": "14469", "NURINTERN": "nein"
          },
          "geometry": { "x": 13.05647, "y": 52.39722 }
        },
        {
          "attributes": {
            "OBJECTID": 2, "GLOBALID": "{BBB}", "BEHOERDE": "Fundbüro",
            "ADRESSE": "Hegelallee 12", "PLZ": "14467"
          },
          "geometry": { "x": 13.05688, "y": 52.39717 }
        }
      ]
    }
    """;

    [Fact]
    public void Reads_name_address_and_position()
    {
        var places = PotsdamPlaceImporter.Parse(Sample);

        places.Should().HaveCount(2);
        var first = places[0];
        first.Name.Should().Be("Bürgerservicecenter");
        first.Address.Should().Be("Friedrich-Ebert-Str. 79/81, 14469");
        first.Lat.Should().BeApproximately(52.39722, 1e-6);
        first.Lon.Should().BeApproximately(13.05647, 1e-6);
        first.ExternalId.Should().Be("{AAA}");
    }

    [Fact]
    public void Same_address_appears_only_once()
    {
        // Mehrere Dienststellen teilen sich oft ein Haus. Als Standort ist das
        // EIN Ort — sonst stapeln sich Marker uebereinander.
        var json = """
        {"features":[
          {"attributes":{"GLOBALID":"{A}","BEHOERDE":"Wohnungsvermittlung","ADRESSE":"Hegelallee 12","PLZ":"14467"},
           "geometry":{"x":13.05873,"y":52.39975}},
          {"attributes":{"GLOBALID":"{B}","BEHOERDE":"Wohnungsaufsicht","ADRESSE":"Hegelallee 12","PLZ":"14467"},
           "geometry":{"x":13.05873,"y":52.39975}}
        ]}
        """;

        PotsdamPlaceImporter.Parse(json).Should().ContainSingle();
    }

    [Fact]
    public void Features_without_geometry_are_skipped()
    {
        // Ein Standort ohne Koordinate kann nicht auf die Karte — er waere ein
        // Bereich, den man nie findet.
        var json = """
        {"features":[
          {"attributes":{"GLOBALID":"{A}","BEHOERDE":"Ohne Ort"}},
          {"attributes":{"GLOBALID":"{B}","BEHOERDE":"Mit Ort","ADRESSE":"X"},"geometry":{"x":13.1,"y":52.4}}
        ]}
        """;

        PotsdamPlaceImporter.Parse(json).Should().ContainSingle()
            .Which.Name.Should().Be("Mit Ort");
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("{}")]
    [InlineData("{\"error\":{\"code\":400}}")]
    [InlineData("{\"features\":[]}")]
    public void Unusable_answers_yield_an_empty_list_instead_of_throwing(string json)
    {
        // Ein Ausfall des fremden Dienstes darf keinen Dialog mit einer
        // Ausnahme zuklappen lassen.
        PotsdamPlaceImporter.Parse(json).Should().BeEmpty();
    }

    [Fact]
    public void Falls_back_to_objectid_when_globalid_is_missing()
    {
        var json = """
        {"features":[{"attributes":{"OBJECTID":42,"BEHOERDE":"Amt","ADRESSE":"Y"},
                      "geometry":{"x":13.1,"y":52.4}}]}
        """;

        PotsdamPlaceImporter.Parse(json).Should().ContainSingle()
            .Which.ExternalId.Should().Be("42");
    }

    [Fact]
    public void Missing_name_gets_a_placeholder_rather_than_being_dropped()
    {
        var json = """
        {"features":[{"attributes":{"GLOBALID":"{A}","ADRESSE":"Z"},"geometry":{"x":13.1,"y":52.4}}]}
        """;

        PotsdamPlaceImporter.Parse(json).Should().ContainSingle()
            .Which.Name.Should().Be("Standort");
    }
}
