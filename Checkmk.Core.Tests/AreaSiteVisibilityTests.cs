using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// LHP und Schul_IT sind heute getrennte Sites, sollen aber irgendwann
/// zusammengeführt werden. Die Site-Zuordnung ist deshalb ein reiner
/// <b>Sichtbarkeitsfilter</b> — ein Ort ist ein Ort, und die Zusammenführung
/// muss ein Löschen der Zuordnungen sein, kein Umbau der Struktur.
/// </summary>
public class AreaSiteVisibilityTests
{
    private static AreaSnapshot Snapshot(params (int AreaId, string[] Sites)[] areas)
        => new(
            [.. areas.Select(a => new AreaRow(a.AreaId, null, $"A{a.AreaId}", 0, null, null))],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            areas.Where(a => a.Sites.Length > 0)
                 .ToDictionary(a => a.AreaId, a => (IReadOnlyList<string>)a.Sites));

    [Fact]
    public void Area_without_site_assignment_is_visible_everywhere()
    {
        // Der wichtigste Fall: Bereiche aus der Zeit vor Schema 4 haben keine
        // Zuordnung und muessen unveraendert sichtbar bleiben.
        var snap = Snapshot((1, []));

        snap.IsVisibleIn(1, "LHP").Should().BeTrue();
        snap.IsVisibleIn(1, "Schul_IT").Should().BeTrue();
        snap.IsVisibleIn(1, "IrgendeineNeueSite").Should().BeTrue();
    }

    [Fact]
    public void Area_assigned_to_one_site_is_hidden_in_the_other()
    {
        var snap = Snapshot((1, ["Schul_IT"]));

        snap.IsVisibleIn(1, "Schul_IT").Should().BeTrue();
        snap.IsVisibleIn(1, "LHP").Should().BeFalse();
    }

    [Fact]
    public void Area_can_belong_to_both_sites()
    {
        // Im Stadthaus steht Technik aus beiden Sites — der Ort ist derselbe.
        var snap = Snapshot((1, ["LHP", "Schul_IT"]));

        snap.IsVisibleIn(1, "LHP").Should().BeTrue();
        snap.IsVisibleIn(1, "Schul_IT").Should().BeTrue();
    }

    [Fact]
    public void Site_comparison_ignores_case()
    {
        var snap = Snapshot((1, ["Schul_IT"]));

        snap.IsVisibleIn(1, "schul_it").Should().BeTrue();
    }

    [Fact]
    public void Without_an_active_site_everything_is_visible()
    {
        // Etwa bevor die Verbindung steht — dann soll die Sicht nicht leer sein.
        var snap = Snapshot((1, ["Schul_IT"]));

        snap.IsVisibleIn(1, null).Should().BeTrue();
        snap.IsVisibleIn(1, "").Should().BeTrue();
    }

    [Fact]
    public void Unknown_area_counts_as_visible()
    {
        // Momentaufnahme aelter als der Baum: lieber zeigen als verschlucken.
        Snapshot((1, ["LHP"])).IsVisibleIn(99, "Schul_IT").Should().BeTrue();
    }

    [Fact]
    public void Clearing_all_assignments_makes_everything_visible_again()
    {
        // So sieht die Zusammenfuehrung der Sites aus: DELETE FROM AreaSite.
        var separated = Snapshot((1, ["LHP"]), (2, ["Schul_IT"]));
        separated.IsVisibleIn(2, "LHP").Should().BeFalse();

        var merged = Snapshot((1, []), (2, []));
        merged.IsVisibleIn(1, "LHP").Should().BeTrue();
        merged.IsVisibleIn(2, "LHP").Should().BeTrue();
    }
}
