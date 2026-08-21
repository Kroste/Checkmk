using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Nachbearbeiten einer gezeichneten Fläche.
///
/// Der Anlass: Lag eine einzige Ecke daneben, musste man die ganze Fläche neu
/// zeichnen — bei einem Campus mit einem Dutzend Ecken dauert das länger als
/// das erste Mal.
/// </summary>
public class MapEditTests
{
    /// <summary>Ein Quadrat, gegen den Uhrzeigersinn.</summary>
    private static readonly GeoPoint[] Square =
    [
        new(13.00, 52.40),
        new(13.02, 52.40),
        new(13.02, 52.42),
        new(13.00, 52.42),
    ];

    // --- Ecke einfügen ---------------------------------------------------

    [Fact]
    public void A_midpoint_lands_between_its_two_neighbours()
    {
        var result = MapGeometry.InsertMidpoint(Square, 0);

        result.Should().HaveCount(5);
        result[1].Should().Be(new GeoPoint(13.01, 52.40));
        // Reihenfolge bleibt erhalten — sonst faltet sich das Polygon.
        result[0].Should().Be(Square[0]);
        result[2].Should().Be(Square[1]);
    }

    [Fact]
    public void The_last_edge_wraps_around_to_the_first_point()
    {
        // Ohne Umlauf haette die schliessende Kante keinen Griff, und genau
        // dort fehlt beim Nachzeichnen am haeufigsten eine Ecke.
        var result = MapGeometry.InsertMidpoint(Square, 3);

        result.Should().HaveCount(5);
        result[4].Should().Be(new GeoPoint(13.00, 52.41));
    }

    [Fact]
    public void Inserting_twice_on_the_same_edge_gives_two_distinct_points()
    {
        var once = MapGeometry.InsertMidpoint(Square, 0);
        var twice = MapGeometry.InsertMidpoint(once, 0);

        twice.Should().HaveCount(6);
        twice.Distinct().Should().HaveCount(6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void An_index_outside_the_polygon_changes_nothing(int edge)
        => MapGeometry.InsertMidpoint(Square, edge).Should().BeEquivalentTo(Square);

    // --- Ecke entfernen --------------------------------------------------

    [Fact]
    public void Removing_a_vertex_keeps_the_rest_in_order()
    {
        var result = MapGeometry.RemoveVertex(Square, 1);

        result.Should().HaveCount(3);
        result.Should().ContainInOrder(Square[0], Square[2], Square[3]);
    }

    [Fact]
    public void A_triangle_cannot_be_reduced_further()
    {
        // Sonst bliebe eine Linie stehen, die als Flaeche gespeichert wuerde —
        // nicht anklickbar und auf der Karte unsichtbar.
        var triangle = MapGeometry.RemoveVertex(Square, 0);
        triangle.Should().HaveCount(3);

        MapGeometry.RemoveVertex(triangle, 0).Should().BeEquivalentTo(triangle);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void An_index_outside_the_polygon_removes_nothing(int index)
        => MapGeometry.RemoveVertex(Square, index).Should().BeEquivalentTo(Square);

    // --- Rundlauf über GeoJSON -------------------------------------------

    [Fact]
    public void An_edited_polygon_survives_the_round_trip_to_geojson()
    {
        // Das ist der Weg, den eine Bearbeitung tatsaechlich nimmt:
        // laden -> aendern -> speichern -> wieder laden.
        var edited = MapGeometry.InsertMidpoint(Square, 0).ToList();
        edited[1] = new GeoPoint(13.015, 52.395);          // Punkt gezogen

        var reloaded = MapGeometry.Parse(MapGeometry.ToGeoJson(edited));

        reloaded.Should().HaveCount(5);
        reloaded[1].Lon.Should().BeApproximately(13.015, 1e-9);
        reloaded[1].Lat.Should().BeApproximately(52.395, 1e-9);
    }

    [Fact]
    public void A_point_moved_outside_still_contains_what_it_should()
    {
        // Nach dem Ziehen muss die Treffererkennung weiter stimmen — sonst
        // laesst sich die Flaeche nicht mehr anklicken.
        var wide = MapGeometry.InsertMidpoint(Square, 0).ToList();
        wide[1] = new GeoPoint(13.01, 52.38);              // Ecke nach unten gezogen

        MapGeometry.Contains(wide, new GeoPoint(13.010, 52.390)).Should().BeTrue();
        MapGeometry.Contains(wide, new GeoPoint(13.010, 52.370)).Should().BeFalse();
    }
}
