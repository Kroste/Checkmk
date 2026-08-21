using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.Data;

namespace Checkmk.App.Views;

/// <summary>
/// Auswahl aus einer externen Standortliste. Bewusst mit Vorauswahl „keine":
/// Die 161 Verwaltungsstandorte der Landeshauptstadt sind Publikumsstellen
/// samt Bibliotheken und Museum — nur ein Teil davon ist für das Monitoring
/// interessant, und alles zu übernehmen macht den Bereichsbaum unbrauchbar.
/// </summary>
public partial class PlaceImportDialog : ChromeWindow
{
    private readonly List<ExternalPlace> _all = [];

    public PlaceImportDialog(string sourceLabel, IReadOnlyList<ExternalPlace> places)
    {
        AvaloniaXamlLoader.Load(this);

        _all = [.. places.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

        Title = $"{sourceLabel} übernehmen";
        this.FindControl<Controls.TitleBar>("DialogTitleBar")!.Title = Title;
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"{_all.Count} Einträge aus „{sourceLabel}“ vom Kartenserver der Landeshauptstadt. "
          + "Auswählen, was als Bereich angelegt werden soll — der Rest lässt sich "
          + "später jederzeit nachholen, ein zweiter Lauf erzeugt keine Dubletten.";

        var filter = this.FindControl<TextBox>("FilterBox")!;
        filter.TextChanged += (_, _) => ApplyFilter(filter.Text);

        var list = this.FindControl<ListBox>("PlaceList")!;
        list.SelectionChanged += (_, _) => UpdateCount();

        ApplyFilter(null);
    }

    // Parameterloser ctor fuer XAML-Designer.
    public PlaceImportDialog() => AvaloniaXamlLoader.Load(this);

    private void ApplyFilter(string? text)
    {
        var list = this.FindControl<ListBox>("PlaceList")!;

        // Auswahl ueber den Filterwechsel retten — wer erst "Schule" sucht,
        // auswaehlt und dann "Amt" tippt, will beide Mengen behalten.
        var selected = list.SelectedItems?.OfType<ExternalPlace>().ToHashSet() ?? [];

        var needle = text?.Trim();
        var visible = string.IsNullOrWhiteSpace(needle)
            ? _all
            : [.. _all.Where(p =>
                p.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || (p.Address?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false))];

        list.ItemsSource = visible;

        if (list.SelectedItems is { } target)
        {
            target.Clear();
            foreach (var p in visible.Where(selected.Contains)) target.Add(p);
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        var list = this.FindControl<ListBox>("PlaceList")!;
        var n = list.SelectedItems?.Count ?? 0;
        this.FindControl<TextBlock>("CountText")!.Text = $"{n} ausgewählt";
    }

    private void OnSelectVisibleClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("PlaceList")!.SelectAll();
        UpdateCount();
    }

    private void OnSelectNoneClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<ListBox>("PlaceList")!.SelectedItems?.Clear();
        UpdateCount();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("PlaceList")!;
        IReadOnlyList<ExternalPlace> chosen = [.. list.SelectedItems?.OfType<ExternalPlace>() ?? []];
        Close(chosen);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
