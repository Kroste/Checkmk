using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class StatusGridColumnsTests
{
    private static ColumnLayout Layout(params (string Key, bool Visible)[] entries)
        => new()
        {
            Columns = [.. entries.Select(e => new ColumnSetting { Key = e.Key, Visible = e.Visible })]
        };

    [Fact]
    public void Empty_layout_falls_back_to_the_default_and_shows_it()
    {
        var merged = StatusGridColumns.Merge(new ColumnLayout());

        merged.Where(c => c.Visible).Select(c => c.Key)
              .Should().Equal(StatusColumnFactory.DefaultLayout);
    }

    [Fact]
    public void Every_catalog_column_is_present_after_merge()
    {
        var merged = StatusGridColumns.Merge(new ColumnLayout());

        merged.Select(c => c.Key).Should().BeEquivalentTo(
            StatusColumnFactory.Catalog.Select(c => c.Key));
    }

    [Fact]
    public void Stored_order_is_preserved()
    {
        var merged = StatusGridColumns.Merge(
            Layout(("service_state", true), ("host", true), ("state_dot", true)));

        merged.Take(3).Select(c => c.Key).Should().Equal("service_state", "host", "state_dot");
    }

    [Fact]
    public void Hidden_stays_hidden()
    {
        var merged = StatusGridColumns.Merge(Layout(("host", true), ("host_alias", false)));

        merged.Single(c => c.Key == "host_alias").Visible.Should().BeFalse();
        merged.Single(c => c.Key == "host").Visible.Should().BeTrue();
    }

    /// <summary>
    /// Eine in einer neuen Version dazugekommene Spalte darf die gewohnte Ansicht
    /// nicht von selbst umbauen — sie haengt ausgeblendet hinten an und steht nur
    /// im Kontextmenue bereit.
    /// </summary>
    [Fact]
    public void Newly_added_catalog_columns_arrive_hidden_at_the_end()
    {
        var merged = StatusGridColumns.Merge(Layout(("host", true), ("service_state", true)));

        merged.Take(2).Select(c => c.Key).Should().Equal("host", "service_state");
        merged.Skip(2).Should().OnlyContain(c => !c.Visible);
        merged.Should().Contain(c => c.Key == "service_display_name" && !c.Visible);
    }

    [Fact]
    public void Unknown_keys_from_an_older_file_are_dropped()
    {
        var merged = StatusGridColumns.Merge(
            Layout(("host", true), ("gibt_es_nicht_mehr", true)));

        merged.Should().NotContain(c => c.Key == "gibt_es_nicht_mehr");
        merged.Should().Contain(c => c.Key == "host" && c.Visible);
    }

    [Fact]
    public void Duplicate_keys_are_collapsed()
    {
        var merged = StatusGridColumns.Merge(
            Layout(("host", true), ("host", false), ("service_state", true)));

        merged.Count(c => c.Key == "host").Should().Be(1);
        merged.Single(c => c.Key == "host").Visible.Should().BeTrue("der erste Eintrag gewinnt");
    }

    /// <summary>Alles unbekannt = so gut wie leer -> Vorgabe, nicht eine leere Tabelle.</summary>
    [Fact]
    public void Layout_with_only_unknown_keys_falls_back_to_the_default()
    {
        var merged = StatusGridColumns.Merge(Layout(("quatsch", true), ("unfug", false)));

        merged.Where(c => c.Visible).Select(c => c.Key)
              .Should().Equal(StatusColumnFactory.DefaultLayout);
    }

    [Fact]
    public void Default_layout_only_uses_known_keys()
        => StatusColumnFactory.DefaultLayout.Should().OnlyContain(k => StatusColumnFactory.IsKnown(k));

    [Fact]
    public void Catalog_has_a_label_for_every_key()
        => StatusColumnFactory.Catalog.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Label));
}

/// <summary>Arbeitet auf einer Wegwerf-Datei — niemals auf
/// <c>%APPDATA%\Kroste\Checkmk\columns.json</c> des angemeldeten Nutzers.</summary>
public class ColumnLayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cockpit-columns-tests-" + Guid.NewGuid().ToString("N"));

    private ColumnLayoutStore NewStore() => new(Path.Combine(_dir, "columns.json"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Roundtrip_keeps_order_visibility_and_width()
    {
        var store = NewStore();
        store.Save("status", new ColumnLayout
        {
            Columns =
            [
                new ColumnSetting { Key = "host", Visible = true, Width = 123.5 },
                new ColumnSetting { Key = "service_state", Visible = false }
            ]
        });

        var loaded = NewStore().Load("status");

        loaded.Columns.Should().HaveCount(2);
        loaded.Columns[0].Key.Should().Be("host");
        loaded.Columns[0].Width.Should().Be(123.5);
        loaded.Columns[1].Visible.Should().BeFalse();
        loaded.Columns[1].Width.Should().BeNull();
    }

    [Fact]
    public void Several_views_live_side_by_side()
    {
        var store = NewStore();
        store.Save("status", new ColumnLayout { Columns = [new ColumnSetting { Key = "host" }] });
        store.Save("andere", new ColumnLayout { Columns = [new ColumnSetting { Key = "service_state" }] });

        store.Load("status").Columns.Should().ContainSingle().Which.Key.Should().Be("host");
        store.Load("andere").Columns.Should().ContainSingle().Which.Key.Should().Be("service_state");
    }

    [Fact]
    public void Reset_removes_only_the_named_view()
    {
        var store = NewStore();
        store.Save("status", new ColumnLayout { Columns = [new ColumnSetting { Key = "host" }] });
        store.Save("andere", new ColumnLayout { Columns = [new ColumnSetting { Key = "host" }] });

        store.Reset("status");

        store.Load("status").IsEmpty.Should().BeTrue();
        store.Load("andere").IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Unknown_view_returns_an_empty_layout()
        => NewStore().Load("gibt-es-nicht").IsEmpty.Should().BeTrue();

    [Fact]
    public void Broken_file_falls_back_to_empty_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "columns.json"), "{ kaputt");

        NewStore().Load("status").IsEmpty.Should().BeTrue();
    }
}
