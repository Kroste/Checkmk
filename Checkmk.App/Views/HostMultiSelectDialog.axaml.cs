using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

/// <summary>
/// Mehrfachauswahl aus einer Hostliste. Gebaut fuer die Bereichszuordnung: Bei
/// 1105 Geraeten ist das Zuweisen einzeln keine Option, also Filter tippen,
/// „Alle sichtbaren" und fertig.
/// </summary>
public partial class HostMultiSelectDialog : ChromeWindow
{
    private readonly List<string> _all = [];

    public HostMultiSelectDialog(string title, IEnumerable<string> hosts)
    {
        AvaloniaXamlLoader.Load(this);

        _all = [.. hosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase)];

        Title = title;
        this.FindControl<TitleBar>("DialogTitleBar")!.Title = title;
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"{_all.Count} Host(s) ohne Bereich. Mehrfachauswahl mit Ctrl/Shift oder Klick.";

        var filter = this.FindControl<TextBox>("FilterBox")!;
        filter.TextChanged += (_, _) => ApplyFilter(filter.Text);

        var list = this.FindControl<ListBox>("HostList")!;
        list.SelectionChanged += (_, _) => UpdateCount();

        ApplyFilter(null);
    }

    // Parameterloser ctor fuer XAML-Designer.
    public HostMultiSelectDialog() => AvaloniaXamlLoader.Load(this);

    private void ApplyFilter(string? text)
    {
        var list = this.FindControl<ListBox>("HostList")!;

        // Auswahl ueber den Filterwechsel retten: Wer erst „sql" tippt, auswaehlt
        // und dann „ora" sucht, will beide Mengen behalten.
        var selected = list.SelectedItems?.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var visible = string.IsNullOrWhiteSpace(text)
            ? _all
            : [.. _all.Where(h => h.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))];

        list.ItemsSource = visible;

        if (list.SelectedItems is { } target)
        {
            target.Clear();
            foreach (var h in visible.Where(selected.Contains))
                target.Add(h);
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        var list = this.FindControl<ListBox>("HostList")!;
        var n = list.SelectedItems?.Count ?? 0;
        this.FindControl<TextBlock>("CountText")!.Text = $"{n} ausgewählt";
    }

    private void OnSelectVisibleClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("HostList")!;
        list.SelectAll();
        UpdateCount();
    }

    private void OnSelectNoneClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("HostList")!;
        list.SelectedItems?.Clear();
        UpdateCount();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("HostList")!;
        IReadOnlyList<string> chosen = [.. list.SelectedItems?.OfType<string>() ?? []];
        Close(chosen);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close(null);
}
