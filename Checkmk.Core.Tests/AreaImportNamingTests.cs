using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Bereichsnamen sind je Ebene eindeutig (Index aus 002-map-teams.sql). Die
/// amtlichen Standortlisten halten sich nicht daran: „Musikschule" steht
/// zweimal drin, an der Galileistraße und in der Jägerstraße. Ohne
/// Entschärfung scheitert der <b>komplette</b> Import an SQL-Fehler 2601, und
/// der Anwender sieht nur „Import fehlgeschlagen" — genau so passiert.
/// </summary>
public class AreaImportNamingTests
{
    private static ExternalPlace Place(string name, string? address = null)
        => new($"id-{name}-{address}", name, 52.4, 13.06, address);

    [Fact]
    public void Free_name_is_kept_unchanged()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place("Stadthaus"), taken).Should().Be("Stadthaus");
    }

    [Fact]
    public void Collision_is_resolved_with_the_street()
    {
        // „Musikschule (Galileistraße 6)" sagt einem Menschen etwas,
        // „Musikschule (2)" nicht.
        var taken = new HashSet<string>(["Musikschule"], StringComparer.OrdinalIgnoreCase);

        var name = AreaStore.UniqueName(Place("Musikschule", "Galileistraße 6, 14480"), taken);

        name.Should().Be("Musikschule (Galileistraße 6)");
    }

    [Fact]
    public void Postcode_is_left_out_of_the_disambiguation()
    {
        var taken = new HashSet<string>(["Amt"], StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place("Amt", "Hegelallee 12, 14467"), taken)
            .Should().NotContain("14467");
    }

    [Fact]
    public void Falls_back_to_counting_when_the_street_collides_too()
    {
        var taken = new HashSet<string>(
            ["Musikschule", "Musikschule (Jägerstraße 3/4)"], StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place("Musikschule", "Jägerstraße 3/4, 14467"), taken)
            .Should().Be("Musikschule (2)");
    }

    [Fact]
    public void Works_without_an_address()
    {
        var taken = new HashSet<string>(["Standort"], StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place("Standort"), taken).Should().Be("Standort (2)");
    }

    [Fact]
    public void Comparison_ignores_case_like_the_database_does()
    {
        // SQL Server vergleicht standardmaessig ohne Ruecksicht auf Gross- und
        // Kleinschreibung — „musikschule" wuerde denselben Index verletzen.
        var taken = new HashSet<string>(["MUSIKSCHULE"], StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place("Musikschule", "Galileistraße 6"), taken)
            .Should().Be("Musikschule (Galileistraße 6)");
    }

    [Fact]
    public void Long_names_are_trimmed_to_the_column_width()
    {
        // Amtliche Schulnamen werden lang: „Berufliche Schule fuer Sport und
        // Gesundheit der Europaeischen Sportakademie des Landes Brandenburg".
        var longName = new string('x', 260);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AreaStore.UniqueName(Place(longName), taken).Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public void Repeated_calls_stay_unique_when_the_caller_tracks_results()
    {
        // So laeuft es im Import: jeder vergebene Name wandert sofort in die Menge.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var places = new[]
        {
            Place("Musikschule", "Galileistraße 6, 14480"),
            Place("Musikschule", "Jägerstraße 3/4, 14467"),
            Place("Musikschule", "Jägerstraße 3/4, 14467")
        };

        var names = new List<string>();
        foreach (var p in places)
        {
            var n = AreaStore.UniqueName(p, taken);
            taken.Add(n);
            names.Add(n);
        }

        names.Should().OnlyHaveUniqueItems();
        names[0].Should().Be("Musikschule");
    }
}
