using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Gemessen: Eine kalte Kachel kostet gut eine Sekunde, aus dem Cache acht
/// Millisekunden — Faktor 680. Der Plan entscheidet, wie viel davon im
/// Hintergrund erledigt wird und wie groß der Cache dabei wird. Beides muss
/// vorhersagbar bleiben, sonst zieht das Vorabladen halb Brandenburg.
/// </summary>
public class MapPrefetchPlannerTests
{
    private static readonly GeoPoint Rathaus = new(13.0570, 52.3963);
    private static readonly GeoPoint Hegelallee = new(13.0530, 52.4020);

    [Fact]
    public void Around_a_point_covers_three_by_three_per_zoom()
    {
        var tiles = MapPrefetchPlanner.AroundPoint(Rathaus, 16, 18).ToList();

        tiles.Should().HaveCount(3 * 3 * 3, "3x3 Kacheln auf drei Zoomstufen");
        tiles.Select(t => t.Zoom).Distinct().Should().BeEquivalentTo(new[] { 16, 17, 18 });
    }

    [Fact]
    public void The_point_itself_is_included()
    {
        var (wx, wy) = WebMercator.ToWorld(Rathaus, 18);
        var expected = WebMercator.TileAt(wx, wy);

        MapPrefetchPlanner.AroundPoint(Rathaus, 18, 18)
            .Should().Contain(t => t.X == expected.X && t.Y == expected.Y);
    }

    [Fact]
    public void Radius_zero_yields_exactly_the_containing_tile()
    {
        MapPrefetchPlanner.AroundPoint(Rathaus, 18, 18, radius: 0).Should().ContainSingle();
    }

    [Fact]
    public void Tiles_outside_the_world_are_dropped()
    {
        // Am Rand der Projektion gaebe es sonst negative Indizes, und der
        // Dienst antwortet darauf mit einer Fehlermeldung statt einem Bild.
        var northPole = new GeoPoint(0, 85.0);

        MapPrefetchPlanner.AroundPoint(northPole, 1, 1, radius: 3)
            .Should().OnlyContain(t => t.X >= 0 && t.Y >= 0 && t.X < 2 && t.Y < 2);
    }

    [Fact]
    public void Bounds_cover_both_corners()
    {
        var min = new GeoPoint(13.00, 52.35);
        var max = new GeoPoint(13.15, 52.45);

        var tiles = MapPrefetchPlanner.ForBounds(min, max, 13, 13).ToList();

        var (x1, y1) = WebMercator.ToWorld(new GeoPoint(min.Lon, max.Lat), 13);
        var (x2, y2) = WebMercator.ToWorld(new GeoPoint(max.Lon, min.Lat), 13);
        tiles.Should().Contain(WebMercator.TileAt(x1, y1) is var a
            ? new TileKey(13, a.X, a.Y) : default);
        tiles.Should().Contain(WebMercator.TileAt(x2, y2) is var b
            ? new TileKey(13, b.X, b.Y) : default);
    }

    [Fact]
    public void Plan_has_no_duplicates_even_for_neighbouring_places()
    {
        // Rathaus und Hegelallee liegen wenige hundert Meter auseinander; auf
        // groben Zoomstufen faellt beides in dieselbe Kachel.
        var plan = MapPrefetchPlanner.Plan([Rathaus, Hegelallee], null);

        plan.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Plan_starts_with_the_coarse_levels()
    {
        // Damit die Uebersicht als Erstes steht und wer waehrenddessen
        // hineinzoomt wenigstens etwas sieht.
        var plan = MapPrefetchPlanner.Plan([Rathaus],
            (new GeoPoint(13.0, 52.35), new GeoPoint(13.15, 52.45)));

        plan.Should().NotBeEmpty();
        plan[0].Zoom.Should().Be(MapPrefetchPlanner.OverviewMinZoom);
        plan.Select(t => t.Zoom).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Plan_for_eighty_places_stays_in_a_sane_range()
    {
        // Der Grund fuer die Begrenzung: Potsdam flaechendeckend bis Zoom 18
        // waeren ueber 40.000 Kacheln JE Ebene. Bei ~100 KB je Kachel waeren
        // das mehrere Gigabyte - pro Kartenhintergrund.
        var places = Enumerable.Range(0, 80)
            .Select(i => new GeoPoint(13.0 + i * 0.002, 52.35 + i * 0.001))
            .ToList();

        var plan = MapPrefetchPlanner.Plan(places,
            (new GeoPoint(12.95, 52.30), new GeoPoint(13.25, 52.48)));

        plan.Count.Should().BeLessThan(5000,
            "sonst laedt das Vorabladen halb Brandenburg");
        plan.Count.Should().BeGreaterThan(500, "zu wenig waere wirkungslos");
    }

    [Fact]
    public void Plan_without_bounds_only_covers_the_places()
    {
        var plan = MapPrefetchPlanner.Plan([Rathaus], null);

        plan.Should().OnlyContain(t => t.Zoom >= MapPrefetchPlanner.DetailMinZoom);
    }
}
