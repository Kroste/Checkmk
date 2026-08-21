using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Geometrie und Projektion der Karte. Beides laesst sich ohne Netz und ohne
/// Kacheldienst pruefen — und muss es auch, weil die Kacheln im Fachbereichsnetz
/// derzeit gar nicht erreichbar sind.
/// </summary>
public class MapGeometryTests
{
    // Grob ein Rechteck ueber der Potsdamer Innenstadt.
    private static readonly GeoPoint[] Square =
    [
        new(13.05, 52.39),
        new(13.07, 52.39),
        new(13.07, 52.41),
        new(13.05, 52.41)
    ];

    [Fact]
    public void Round_trip_through_geojson_keeps_the_points()
    {
        var json = MapGeometry.ToGeoJson(Square);

        var back = MapGeometry.Parse(json);

        back.Should().Equal(Square);
    }

    [Fact]
    public void Written_geojson_closes_the_ring()
    {
        // Der Standard verlangt den doppelten ersten Punkt. Intern arbeiten wir
        // ohne ihn — beim Schreiben muss er wieder dran.
        var json = MapGeometry.ToGeoJson(Square)!;

        json.Should().Contain("\"type\":\"Polygon\"");
        json.Should().EndWith("[13.05,52.39]]]}");
    }

    [Fact]
    public void Feature_wrapper_is_accepted()
    {
        // Uebliche Werkzeuge exportieren ein Feature statt einer nackten
        // Geometrie. Daran soll niemand scheitern.
        var feature = """
            {"type":"Feature","properties":{},
             "geometry":{"type":"Polygon","coordinates":[[[13.05,52.39],[13.07,52.39],[13.07,52.41],[13.05,52.39]]]}}
            """;

        MapGeometry.Parse(feature).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("{\"type\":\"Polygon\"}")]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[13.05,52.39]}")]
    public void Broken_or_unsupported_input_yields_no_points_instead_of_throwing(string? input)
    {
        // Ein kaputtes Polygon darf die Karte nicht sprengen — der Bereich
        // erscheint ohne Flaeche und laesst sich neu zeichnen.
        MapGeometry.Parse(input).Should().BeEmpty();
    }

    [Fact]
    public void Too_few_points_is_not_an_area()
    {
        MapGeometry.ToGeoJson([new GeoPoint(13, 52), new GeoPoint(14, 52)]).Should().BeNull();
    }

    [Fact]
    public void Contains_finds_points_inside_and_rejects_those_outside()
    {
        MapGeometry.Contains(Square, new GeoPoint(13.06, 52.40)).Should().BeTrue();
        MapGeometry.Contains(Square, new GeoPoint(13.10, 52.40)).Should().BeFalse();
        MapGeometry.Contains(Square, new GeoPoint(13.06, 52.50)).Should().BeFalse();
    }

    [Fact]
    public void Contains_on_a_degenerate_polygon_is_false_not_a_crash()
    {
        MapGeometry.Contains([], new GeoPoint(13, 52)).Should().BeFalse();
        MapGeometry.Contains([new GeoPoint(13, 52)], new GeoPoint(13, 52)).Should().BeFalse();
    }

    [Fact]
    public void Bounds_span_the_polygon()
    {
        var bounds = MapGeometry.Bounds(Square);

        bounds.Should().NotBeNull();
        bounds!.Value.Min.Should().Be(new GeoPoint(13.05, 52.39));
        bounds.Value.Max.Should().Be(new GeoPoint(13.07, 52.41));
    }

    // --- Projektion -----------------------------------------------------

    [Fact]
    public void World_projection_round_trips()
    {
        var p = new GeoPoint(13.06, 52.40);

        var (x, y) = WebMercator.ToWorld(p, 13);
        var back = WebMercator.ToGeo(x, y, 13);

        back.Lon.Should().BeApproximately(p.Lon, 1e-9);
        back.Lat.Should().BeApproximately(p.Lat, 1e-9);
    }

    [Fact]
    public void Potsdam_lands_on_the_expected_tile()
    {
        // Gegenprobe gegen die bekannte Slippy-Map-Formel:
        //   x = floor((lon+180)/360 · 2^z)
        //   y = floor((1 − ln(tan φ + sec φ)/π)/2 · 2^z)
        // Fuer Potsdam auf Zoom 13 ergibt das Kachel 4393/2691. Faellt sofort
        // auf, wenn jemand Vorzeichen oder Achsen vertauscht.
        var (x, y) = WebMercator.ToWorld(new GeoPoint(13.06, 52.40), 13);

        WebMercator.TileAt(x, y).Should().Be((4393, 2691));
    }

    [Fact]
    public void Origin_maps_to_the_centre_of_the_world()
    {
        var (x, y) = WebMercator.ToWorld(new GeoPoint(0, 0), 0);

        x.Should().BeApproximately(128, 1e-6);
        y.Should().BeApproximately(128, 1e-6);
    }

    [Fact]
    public void Latitude_beyond_the_mercator_limit_is_clamped_not_infinite()
    {
        // Ohne Kappung laeuft die Formel am Pol gegen unendlich und die
        // Zeichenflaeche bekommt NaN-Koordinaten. MaxLatitude ist genau die
        // Breite, bei der y = 0 wird — bis auf Rundung im letzten Bit.
        var (_, y) = WebMercator.ToWorld(new GeoPoint(0, 89.9), 5);

        double.IsFinite(y).Should().BeTrue();
        y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Fit_zoom_gets_closer_for_a_smaller_area()
    {
        var city = WebMercator.FitZoom(new GeoPoint(13.0, 52.3), new GeoPoint(13.2, 52.5), 800, 600);
        var room = WebMercator.FitZoom(new GeoPoint(13.06, 52.40), new GeoPoint(13.061, 52.401), 800, 600);

        room.Should().BeGreaterThan(city);
        city.Should().BeInRange(0, 21);
        room.Should().BeInRange(0, 21);
    }
}
