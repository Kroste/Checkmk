using Checkmk.App.Services;
using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Die Namenskonvention aus dem Betrieb: Die Schulnummer steckt im Hostnamen,
/// aber an wechselnder Stelle — <c>46-SW04</c>, <c>46-USV</c>, <c>NAS46-01</c>,
/// <c>PA46-01</c>, <c>ESX46-02</c>, <c>iRMC-46</c>, <c>WLC-46…</c>.
/// Ein simples „enthält 46" wäre falsch: Es träfe auch <c>146-SW01</c>.
/// </summary>
public class HostPatternTests
{
    [Theory]
    [InlineData("46-SW04")]
    [InlineData("46-USV")]
    [InlineData("NAS46-01")]
    [InlineData("PA46-02")]
    [InlineData("ESX46-01")]
    [InlineData("iRMC-46")]
    [InlineData("WLC-46")]
    public void School_number_is_found_wherever_it_sits(string host)
    {
        var pattern = HostPatternMatcher.FromCode("46");

        HostPatternMatcher.Matches(pattern, host).Should().BeTrue();
    }

    [Theory]
    [InlineData("146-SW01")]   // Schule 146, nicht 46
    [InlineData("460-SW01")]   // Schule 460
    [InlineData("4-SW01")]     // Schule 4
    [InlineData("36-SW02")]
    public void Neighbouring_numbers_are_not_swept_up(string host)
    {
        // Ohne Ziffern-Grenze bekaeme Schule 4 alle Hosts der Schulen 40-49,
        // und Schule 46 die von 146 und 460.
        var pattern = HostPatternMatcher.FromCode("46");

        HostPatternMatcher.Matches(pattern, host).Should().BeFalse();
    }

    [Fact]
    public void School_four_does_not_match_school_fortysix()
    {
        var four = HostPatternMatcher.FromCode("4");

        HostPatternMatcher.Matches(four, "4-SW01").Should().BeTrue();
        HostPatternMatcher.Matches(four, "46-SW04").Should().BeFalse();
        HostPatternMatcher.Matches(four, "NAS4-01").Should().BeTrue();
    }

    [Theory]
    // Zusammengelegte Schulen stehen mit zwei Nummern in den Daten, benutzt wird
    // aber nur eine — und welche, steht nirgends. Angabe des Fachbereichs:
    [InlineData("25/26", "25")]   // Karl-Foerster-Schule
    [InlineData("10/30", "30")]   // Schule am Nuthetal
    [InlineData("42/44", "44")]   // Wilhelm-von-Türk-Schule
    [InlineData("36/45", "36")]   // Grundschule Am Pappelhain
    public void Combined_schools_use_the_number_that_is_actually_in_use(string code, string expected)
    {
        PotsdamPlaceImporter.EffectiveCode(code).Should().Be(expected);

        var pattern = PotsdamPlaceImporter.PatternFor(code);
        HostPatternMatcher.Matches(pattern, $"{expected}-SW01").Should().BeTrue();
    }

    [Fact]
    public void The_unused_half_of_a_combined_code_is_not_claimed()
    {
        // Frueher erzeugte der Import eine Alternative (?:25|26) und haette
        // damit Hosts beansprucht, die es unter 26 gar nicht gibt — und im
        // schlechteren Fall die einer anderen Schule.
        var pattern = PotsdamPlaceImporter.PatternFor("25/26");

        HostPatternMatcher.Matches(pattern, "25-SW01").Should().BeTrue();
        HostPatternMatcher.Matches(pattern, "26-USV").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_combined_code_yields_no_pattern_instead_of_a_guess()
    {
        // Raten hiesse, die Hosts einer fremden Nummer zu beanspruchen. Ein
        // fehlendes Muster faellt dagegen daran auf, dass keine Vorschlaege
        // kommen, und ist in zehn Sekunden nachgetragen.
        PotsdamPlaceImporter.EffectiveCode("77/88").Should().BeNull();
        PotsdamPlaceImporter.PatternFor("77/88").Should().BeNull();
    }

    [Fact]
    public void Plain_numbers_pass_through_the_importer_unchanged()
    {
        PotsdamPlaceImporter.EffectiveCode("46").Should().Be("46");
        PotsdamPlaceImporter.EffectiveCode("SFT").Should().BeNull();
        PotsdamPlaceImporter.EffectiveCode("OSZ III").Should().BeNull();
    }

    [Theory]
    [InlineData("SFT")]       // Schule freier Traeger
    [InlineData("F26")]       // berufliche Schule
    [InlineData("OSZ III")]
    [InlineData("")]
    [InlineData(null)]
    public void Codes_without_a_number_yield_no_pattern(string? code)
    {
        // Freie Traeger und OSZ haben keine staedtische Nummer - dort gibt es
        // nichts abzuleiten, und ein falsches Muster waere schlimmer als keins.
        HostPatternMatcher.FromCode(code).Should().BeNull();
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        HostPatternMatcher.Matches(HostPatternMatcher.FromCode("46"), "irmc-46").Should().BeTrue();
        HostPatternMatcher.Matches("stadthaus", "SW-STADTHAUS-01").Should().BeTrue();
    }

    [Fact]
    public void Broken_pattern_matches_nothing_instead_of_throwing()
    {
        // Ein Tippfehler im von Hand gepflegten Muster darf keinen Ablauf
        // sprengen - er soll daran auffallen, dass keine Vorschlaege kommen.
        HostPatternMatcher.Matches("(unbalanced", "46-SW04").Should().BeFalse();
        HostPatternMatcher.IsValid("(unbalanced").Should().BeFalse();
        HostPatternMatcher.IsValid(@"(?<!\d)46(?!\d)").Should().BeTrue();
    }

    // --- Vorschläge ------------------------------------------------------

    private static AreaRow Area(int id, string name, string? pattern)
        => new(id, null, name, 0, null, null, HostPattern: pattern);

    [Fact]
    public void Suggests_unassigned_hosts_for_their_area()
    {
        var areas = new[] { Area(1, "Schule 46", HostPatternMatcher.FromCode("46")) };

        var result = AreaAssignmentSuggester.Suggest(areas,
            ["46-SW04", "46-USV", "36-SW02"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        result.Select(s => s.HostName).Should().BeEquivalentTo(["46-SW04", "46-USV"]);
        result.Should().OnlyContain(s => s.AreaId == 1 && !s.WouldMove && !s.IsAmbiguous);
    }

    [Fact]
    public void Already_correctly_assigned_hosts_are_not_suggested_again()
    {
        var areas = new[] { Area(1, "Schule 46", HostPatternMatcher.FromCode("46")) };
        var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["46-SW04"] = 1
        };

        AreaAssignmentSuggester.Suggest(areas, ["46-SW04"], assigned).Should().BeEmpty();
    }

    [Fact]
    public void A_host_assigned_elsewhere_is_flagged_as_a_move()
    {
        var areas = new[]
        {
            Area(1, "Schule 46", HostPatternMatcher.FromCode("46")),
            Area(2, "Container", null)
        };
        var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["46-SW04"] = 2
        };

        var s = AreaAssignmentSuggester.Suggest(areas, ["46-SW04"], assigned).Single();

        s.WouldMove.Should().BeTrue();
        s.CurrentAreaName.Should().Be("Container");
        s.Note.Should().Contain("verschiebt");
    }

    [Fact]
    public void Overlapping_patterns_are_reported_as_ambiguous_not_guessed()
    {
        // Zwei Muster treffen denselben Host. Automatisch das erste zu nehmen
        // waere eine stille Fehlzuordnung.
        var areas = new[]
        {
            Area(1, "Schule 46", HostPatternMatcher.FromCode("46")),
            Area(2, "Sammelbereich", "SW")
        };

        var s = AreaAssignmentSuggester.Suggest(areas, ["46-SW04"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)).Single();

        s.IsAmbiguous.Should().BeTrue();
        s.ConflictingAreas.Should().Contain("Sammelbereich");
        s.Note.Should().Contain("mehrdeutig");
    }

    [Fact]
    public void Areas_without_a_pattern_never_produce_suggestions()
    {
        var areas = new[] { Area(1, "Ohne Muster", null) };

        AreaAssignmentSuggester.Suggest(areas, ["46-SW04"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)).Should().BeEmpty();
    }
}
