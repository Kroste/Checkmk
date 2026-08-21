using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

/// <summary>
/// Der einmalige Abgleich „welcher Checkmk-Ortstag gehört zu welchem Bereich",
/// zum Durchsehen.
///
/// Dieser Dialog ist die Stelle, an der geraten werden <b>darf</b>: Die
/// Übersetzung Schulnummer → Tag ist unregelmäßig (<c>schule_2526</c> für
/// 25/26, aber <c>schule_10</c> für 10/30), und eine Regel, die alle Fälle
/// automatisch trifft, träfe irgendwann auch den falschen Bereich, ohne dass
/// es jemandem auffiele. Hier steht das Ergebnis vor Augen, wird bestätigt und
/// danach als exakter Wert gespeichert.
///
/// Vorausgewählt sind nur die <b>eindeutigen</b> Treffer. Mehrdeutige bleiben
/// eine bewusste Entscheidung.
/// </summary>
public partial class TagMatchDialog : ChromeWindow
{
    private readonly List<TagMatch> _all = [];

    public TagMatchDialog(IReadOnlyList<TagMatch> matches)
    {
        AvaloniaXamlLoader.Load(this);

        _all = [.. matches];

        var ambiguous = _all.Count(m => m.IsAmbiguous);
        var replacements = _all.Count(m => !m.IsUnchanged && m.CurrentTag is not null);
        var hosts = _all.Where(m => !m.IsAmbiguous).Sum(m => m.HostCount);

        this.FindControl<TextBlock>("PromptText")!.Text =
            $"{_all.Count} Ortstags lassen sich einem Bereich zuordnen — zusammen {hosts} Hosts. "
          + $"Davon {ambiguous} mehrdeutige und {replacements}, die einen bestehenden Tag ersetzen — "
          + "beide sind nicht vorausgewählt. Übernommen wird nur die Zuordnung Tag→Bereich; "
          + "die Hosts selbst ordnet danach „Zuordnung vorschlagen…“ zu.";

        var filter = this.FindControl<TextBox>("FilterBox")!;
        filter.TextChanged += (_, _) => ApplyFilter();

        var onlyChanges = this.FindControl<CheckBox>("OnlyChangesBox")!;
        onlyChanges.IsCheckedChanged += (_, _) => ApplyFilter();

        var list = this.FindControl<ListBox>("MatchList")!;
        list.SelectionChanged += (_, _) => UpdateCount();

        ApplyFilter();
        PreselectUnambiguous();
    }

    // Parameterloser ctor fuer XAML-Designer.
    public TagMatchDialog() => AvaloniaXamlLoader.Load(this);

    private IEnumerable<TagMatch> Visible()
    {
        var needle = this.FindControl<TextBox>("FilterBox")!.Text?.Trim();
        var onlyChanges = this.FindControl<CheckBox>("OnlyChangesBox")!.IsChecked == true;

        return _all
            .Where(m => !onlyChanges || !m.IsUnchanged)
            .Where(m => string.IsNullOrWhiteSpace(needle)
                     || m.TagValue.Contains(needle, StringComparison.OrdinalIgnoreCase)
                     || m.AreaName.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter()
    {
        var list = this.FindControl<ListBox>("MatchList")!;
        var selected = list.SelectedItems?.OfType<TagMatch>().ToHashSet() ?? [];

        var visible = Visible().ToList();
        list.ItemsSource = visible;

        // Auswahl ueber den Filterwechsel retten.
        if (list.SelectedItems is { } target)
        {
            target.Clear();
            foreach (var m in visible.Where(selected.Contains)) target.Add(m);
        }
        UpdateCount();
    }

    private void PreselectUnambiguous()
    {
        var list = this.FindControl<ListBox>("MatchList")!;
        if (list.SelectedItems is not { } target) return;

        target.Clear();
        foreach (var m in Visible().Where(m => !m.IsAmbiguous && m.CurrentTag is null))
            target.Add(m);
        UpdateCount();
    }

    private void UpdateCount()
    {
        var list = this.FindControl<ListBox>("MatchList")!;
        var chosen = list.SelectedItems?.OfType<TagMatch>().ToList() ?? [];
        this.FindControl<TextBlock>("CountText")!.Text =
            chosen.Count == 0 ? "nichts ausgewählt"
                              : $"{chosen.Count} Tag(s) → {chosen.Sum(m => m.HostCount)} Hosts";
    }

    private void OnSelectVisibleClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("MatchList")!.SelectAll();
        UpdateCount();
    }

    private void OnSelectNoneClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("MatchList")!.SelectedItems?.Clear();
        UpdateCount();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("MatchList")!;

        // Zwei Bereiche mit demselben Tag scheitern am eindeutigen Index — das
        // waere eine unlesbare Serverfehlermeldung fuer einen Bedienfehler.
        // Deshalb hier abfangen, bevor geschrieben wird.
        IReadOnlyList<TagMatch> chosen = [.. list.SelectedItems?.OfType<TagMatch>() ?? []];
        var clash = chosen.GroupBy(m => m.AreaId).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            this.FindControl<TextBlock>("CountText")!.Text =
                $"„{clash.First().AreaName}“ bekäme {clash.Count()} Tags "
              + $"({string.Join(", ", clash.Select(m => m.TagValue))}) — nur einen auswählen.";
            return;
        }

        Close(chosen);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
