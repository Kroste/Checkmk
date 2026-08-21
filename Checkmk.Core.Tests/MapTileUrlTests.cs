using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der Kachel-URL entscheidet, ob der WMS ein Luftbild oder eine
/// XML-Fehlermeldung liefert. Die erwarteten Werte stammen aus einem echten
/// Abruf gegen den Dienst der LGB — die Kachel z13/4393/2691 (Potsdam
/// Innenstadt) lieferte damit 156 KB Bilddaten.
/// </summary>
public class MapTileUrlTests
{
    private const string Wms = "https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms";
    private const string Layer = "bebb_dop20c";

    [Fact]
    public void Potsdam_tile_gets_the_verified_bounding_box()
    {
        var url = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4393, 2691));

        url.Should().Contain("BBOX=1452915.034,6868325.614,1457807.003,6873217.583");
    }

    [Fact]
    public void Uses_wms_1_1_1_with_srs_not_1_3_0_with_crs()
    {
        // In WMS 1.3.0 haengt die Achsenreihenfolge vom Koordinatensystem ab.
        // Daran vertauschen sich Laenge und Breite lautlos, und die Karte zeigt
        // die falsche Weltgegend statt einen Fehler.
        var url = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4393, 2691));

        url.Should().Contain("VERSION=1.1.1");
        // Der Doppelpunkt wird kodiert — gegen alle sechs Dienste geprueft,
        // beide Schreibweisen liefern Kacheln.
        url.Should().Contain("SRS=EPSG%3A3857");
        url.Should().NotContain("CRS=");
    }

    [Fact]
    public void Requests_a_square_tile_of_the_expected_size()
    {
        var url = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4393, 2691));

        url.Should().Contain("WIDTH=256").And.Contain("HEIGHT=256");
        url.Should().Contain("FORMAT=image/png");
        url.Should().Contain($"LAYERS={Layer}");
    }

    [Fact]
    public void Zoom_zero_covers_the_whole_world()
    {
        var url = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(0, 0, 0));

        url.Should().Contain("BBOX=-20037508.343,-20037508.343,20037508.343,20037508.343");
    }

    [Fact]
    public void Neighbouring_tiles_share_an_edge()
    {
        // Luecken oder Ueberlappungen zwischen Kacheln faellt man sonst erst am
        // fertigen Kartenbild auf, und dann sucht man lange.
        var left = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4393, 2691));
        var right = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4394, 2691));

        var leftMaxX = Bbox(left)[2];
        var rightMinX = Bbox(right)[0];

        rightMinX.Should().BeApproximately(leftMaxX, 0.01);
    }

    [Fact]
    public void Base_url_that_already_has_a_query_gets_an_ampersand()
    {
        // MapProxy-Adressen tragen gelegentlich schon einen Parameter.
        var url = MapTileLoader.BuildUrl(Wms + "?map=dop", Layer, new TileKey(5, 1, 1));

        url.Should().Contain("?map=dop&REQUEST=GetMap");
        url.Should().NotContain("??");
    }

    [Fact]
    public void Layer_names_are_url_encoded()
    {
        var url = MapTileLoader.BuildUrl(Wms, "ein layer", new TileKey(5, 1, 1));

        url.Should().Contain("LAYERS=ein%20layer");
    }

    // --- Dienste ohne Web-Mercator -------------------------------------

    [Fact]
    public void Geographic_service_gets_degrees_in_lon_lat_order()
    {
        // Der Kartenserver der Landeshauptstadt Potsdam kann nur EPSG:4326.
        // WMS 1.1.1 erwartet dort Laenge, Breite — in 1.3.0 waere es umgekehrt,
        // und die Karte zeigte Somalia statt Potsdam.
        var url = MapTileLoader.BuildUrl(
            "https://geoportal.potsdam.de/server/services/Stadtkarte/MapServer/WMSServer",
            "0,1,2", new TileKey(13, 4393, 2691), "EPSG:4326");

        url.Should().Contain("SRS=EPSG%3A4326");

        var bbox = Bbox(url);
        bbox[0].Should().BeApproximately(13.0518, 0.001);   // minLon
        bbox[2].Should().BeApproximately(13.0957, 0.001);   // maxLon
        bbox[1].Should().BeInRange(52.3, 52.5);             // minLat
        bbox[3].Should().BeInRange(52.3, 52.5);             // maxLat
        bbox[3].Should().BeGreaterThan(bbox[1], "maxLat gehoert hinter minLat");
    }

    [Fact]
    public void Mercator_stays_the_default_when_no_crs_is_given()
    {
        var url = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(13, 4393, 2691));

        url.Should().Contain("SRS=EPSG%3A3857");
        Bbox(url)[0].Should().BeApproximately(1452915.034, 0.01);
    }

    [Fact]
    public void Geographic_tiles_also_share_their_edges()
    {
        var upper = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(18, 140581, 86123), "EPSG:4326");
        var lower = MapTileLoader.BuildUrl(Wms, Layer, new TileKey(18, 140581, 86124), "EPSG:4326");

        // Untere Kachel endet oben genau dort, wo die obere unten aufhoert.
        Bbox(lower)[3].Should().BeApproximately(Bbox(upper)[1], 1e-9);
    }

    [Fact]
    public void Multiple_layers_are_passed_through_comma_separated()
    {
        // ALKIS ist auf Fachthemen aufgeteilt; die Liegenschaftskarte entsteht
        // erst aus der Kombination.
        var url = MapTileLoader.BuildUrl("https://isk.geobasis-bb.de/ows/alkis_wms",
            "adv_alkis_flurstuecke,adv_alkis_gebaeude", new TileKey(18, 140581, 86123));

        url.Should().Contain("LAYERS=adv_alkis_flurstuecke%2Cadv_alkis_gebaeude");
    }

    private static double[] Bbox(string url)
    {
        var part = url.Split("BBOX=")[1].Split('&')[0];
        return [.. part.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
    }
}
