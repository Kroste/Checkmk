using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Teams und geteilte Filter. Die Regeln hier sind bewusst großzügig — Teams
/// sind <b>Organisation, kein Zugriffsschutz</b>. Alle 48 Personen dürfen alle
/// Hosts sehen; die echte Grenze ist die Checkmk-Rolle, nicht diese Tabelle.
/// </summary>
public class TeamFilterTests
{
    private static TeamRow Team(int id, string name, params string[] members)
        => new(id, name, null, members);

    // --- Admin-Regel -----------------------------------------------------

    [Fact]
    public void An_empty_admin_table_makes_everyone_admin()
    {
        // Eine leere Tabelle heisst „noch nicht eingerichtet". Die Alternative
        // waere eine Funktion, die ohne einen SQL-Eingriff niemand benutzen
        // kann — und Teams sind ausdruecklich kein Zugriffsschutz.
        var snap = new TeamSnapshot([], []);

        snap.IsAdmin("OsteL").Should().BeTrue();
        snap.IsAdmin("irgendwer").Should().BeTrue();
    }

    [Fact]
    public void Once_an_admin_is_named_the_list_applies()
    {
        var snap = new TeamSnapshot([], ["OsteL"]);

        snap.IsAdmin("OsteL").Should().BeTrue();
        snap.IsAdmin("ostel").Should().BeTrue();      // Anmeldenamen sind case-insensitive
        snap.IsAdmin("jemand").Should().BeFalse();
    }

    // --- Mitgliedschaft --------------------------------------------------

    [Fact]
    public void Membership_is_many_to_many()
    {
        // Wer AD und Exchange macht, steht in beiden Teams.
        var snap = new TeamSnapshot(
            [Team(1, "AD", "OsteL"), Team(2, "Exchange", "OsteL"), Team(3, "DB", "wer")],
            []);

        snap.TeamsOf("OsteL").Select(t => t.Name).Should().BeEquivalentTo(["AD", "Exchange"]);
    }

    [Fact]
    public void Someone_in_no_team_belongs_to_none()
    {
        var snap = new TeamSnapshot([Team(1, "AD", "wer")], []);

        snap.TeamsOf("OsteL").Should().BeEmpty();
    }

    [Fact]
    public void A_renamed_team_is_found_by_id()
    {
        var snap = new TeamSnapshot([Team(7, "Netzwerk", "OsteL")], []);

        snap.NameOf(7).Should().Be("Netzwerk");
        snap.NameOf(99).Should().BeNull();
        snap.NameOf(null).Should().BeNull();
    }

    [Fact]
    public void A_team_without_members_says_so()
    {
        // Sonst sieht ein frisch angelegtes Team wie ein fertiges aus, und
        // niemand merkt, dass die Zuordnung noch fehlt.
        Team(1, "Backup").Display.Should().Contain("keine Mitglieder");
        Team(1, "Backup", "a", "b").Display.Should().Be("Backup (2)");
    }

    // --- Sichtbarkeit von Filtern ---------------------------------------

    /// <summary>
    /// Bildet die Auswahlregel aus <c>FilterStore.LoadAsync</c> nach: eigene
    /// persönliche Filter plus die Filter der eigenen Teams — und wer in keinem
    /// Team ist, sieht alle Team-Filter.
    /// </summary>
    private static IReadOnlyList<string> Visible(
        TeamSnapshot snap, string user, IReadOnlyList<HostFilterRow> all)
    {
        var mine = snap.TeamsOf(user).Select(t => t.TeamId).ToHashSet();
        return [.. all
            .Where(f => (f.OwnerUserName is { } o
                         && o.Equals(user, StringComparison.OrdinalIgnoreCase))
                     || (f.TeamId is { } t && (mine.Count == 0 || mine.Contains(t))))
            .Select(f => f.Name)];
    }

    private static HostFilterRow Personal(string name, string owner)
        => new() { Name = name, OwnerUserName = owner, Site = "LHP" };

    private static HostFilterRow Shared(string name, int teamId)
        => new() { Name = name, TeamId = teamId, Site = "LHP" };

    [Fact]
    public void A_team_member_sees_own_and_team_filters_but_not_foreign_personal_ones()
    {
        var snap = new TeamSnapshot([Team(1, "Netzwerk", "OsteL"), Team(2, "DB", "wer")], []);
        var all = new[]
        {
            Personal("meiner", "OsteL"),
            Personal("fremder", "wer"),
            Shared("Netz-Switche", 1),
            Shared("DB-Server", 2),
        };

        Visible(snap, "OsteL", all).Should().BeEquivalentTo(["meiner", "Netz-Switche"]);
    }

    [Fact]
    public void Someone_in_no_team_sees_all_team_filters_instead_of_none()
    {
        // Dieselbe Regel wie beim Bereichsbaum: keine Zuordnung heisst „alles",
        // nicht „nichts". Sonst stuende ein neuer Kollege vor einer leeren
        // Liste und baute sich alles noch einmal.
        var snap = new TeamSnapshot([Team(1, "Netzwerk", "wer"), Team(2, "DB", "wer")], []);
        var all = new[]
        {
            Personal("meiner", "OsteL"),
            Personal("fremder", "wer"),
            Shared("Netz-Switche", 1),
            Shared("DB-Server", 2),
        };

        Visible(snap, "OsteL", all).Should()
            .BeEquivalentTo(["meiner", "Netz-Switche", "DB-Server"]);
    }

    [Fact]
    public void A_personal_filter_stays_personal_even_with_the_same_name()
    {
        var snap = new TeamSnapshot([], []);
        var all = new[] { Personal("Meine Sicht", "OsteL"), Personal("Meine Sicht", "wer") };

        Visible(snap, "wer", all).Should().HaveCount(1);
    }
}
