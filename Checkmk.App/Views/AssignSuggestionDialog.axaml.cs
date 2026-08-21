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
/// Zeigt die Zuordnungsvorschläge zum Durchsehen. Bewusst mit Bestätigung:
/// Ein Muster kann danebenliegen, und tausend falsche Zuordnungen hinterher
/// aufzuräumen ist teurer, als sie einmal anzusehen.
///
/// Vorausgewählt sind nur die **eindeutigen neuen** Vorschläge. Wer schon
/// woanders steht oder auf mehrere Muster passt, bleibt eine bewusste
/// Entscheidung.
/// </summary>
public partial class AssignSuggestionDialog : ChromeWindow
{
    private readonly List<AssignmentSuggestion> _all = [];

    public AssignSuggestionDialog(IReadOnlyList<AssignmentSuggestion> suggestions)
    {
        AvaloniaXamlLoader.Load(this);

        _all = [.. suggestions];

        var moves = _all.Count(s => s.WouldMove);
        var ambiguous = _all.Count(s => s.IsAmbiguous);
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"{_all.Count} Vorschläge aus den Namensmustern der Bereiche. "
          + $"Davon {moves} Verschiebungen bereits zugeordneter Hosts und {ambiguous} mehrdeutige — "
          + "beide sind nicht vorausgewählt.";

        var filter = this.FindControl<TextBox>("FilterBox")!;
        filter.TextChanged += (_, _) => ApplyFilter();

        var onlyNew = this.FindControl<CheckBox>("OnlyNewBox")!;
        onlyNew.IsCheckedChanged += (_, _) => ApplyFilter();

        var list = this.FindControl<ListBox>("SuggestionList")!;
        list.SelectionChanged += (_, _) => UpdateCount();

        ApplyFilter();
        PreselectUnambiguous();
    }

    // Parameterloser ctor fuer XAML-Designer.
    public AssignSuggestionDialog() => AvaloniaXamlLoader.Load(this);

    private IEnumerable<AssignmentSuggestion> Visible()
    {
        var needle = this.FindControl<TextBox>("FilterBox")!.Text?.Trim();
        var onlyNew = this.FindControl<CheckBox>("OnlyNewBox")!.IsChecked == true;

        return _all
            .Where(s => !onlyNew || !s.WouldMove)
            .Where(s => string.IsNullOrWhiteSpace(needle)
                     || s.HostName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                     || s.AreaName.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter()
    {
        var list = this.FindControl<ListBox>("SuggestionList")!;
        var selected = list.SelectedItems?.OfType<AssignmentSuggestion>().ToHashSet() ?? [];

        var visible = Visible().ToList();
        list.ItemsSource = visible;

        // Auswahl ueber den Filterwechsel retten.
        if (list.SelectedItems is { } target)
        {
            target.Clear();
            foreach (var s in visible.Where(selected.Contains)) target.Add(s);
        }
        UpdateCount();
    }

    private void PreselectUnambiguous()
    {
        var list = this.FindControl<ListBox>("SuggestionList")!;
        if (list.SelectedItems is not { } target) return;

        target.Clear();
        foreach (var s in Visible().Where(s => !s.IsAmbiguous && !s.WouldMove))
            target.Add(s);
        UpdateCount();
    }

    private void UpdateCount()
    {
        var list = this.FindControl<ListBox>("SuggestionList")!;
        var chosen = list.SelectedItems?.OfType<AssignmentSuggestion>().ToList() ?? [];
        var areas = chosen.Select(s => s.AreaId).Distinct().Count();
        this.FindControl<TextBlock>("CountText")!.Text =
            chosen.Count == 0 ? "nichts ausgewählt"
                              : $"{chosen.Count} Host(s) → {areas} Bereich(e)";
    }

    private void OnSelectVisibleClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("SuggestionList")!.SelectAll();
        UpdateCount();
    }

    private void OnSelectNoneClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("SuggestionList")!.SelectedItems?.Clear();
        UpdateCount();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("SuggestionList")!;
        IReadOnlyList<AssignmentSuggestion> chosen =
            [.. list.SelectedItems?.OfType<AssignmentSuggestion>() ?? []];
        Close(chosen);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
