using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

/// <summary>Auswahl des Anwenders: welche Liste, und für welche Sites.</summary>
/// <param name="Sites">Leer = in allen Sites sichtbar.</param>
public sealed record PlaceSourceChoice(PlaceSource Source, IReadOnlyList<string> Sites);

public partial class PlaceSourceDialog : ChromeWindow
{
    /// <summary>Zeigt das Label statt der Kennung — <c>PlaceSource</c> ist ein
    /// Record, dessen ToString sonst alle Felder ausschreibt.</summary>
    private sealed record Entry(PlaceSource Source)
    {
        public override string ToString() => Source.Label;
    }

    /// <param name="knownSites">Sites aus den Verbindungseinstellungen.</param>
    /// <param name="preselect">Vorauswahl — die gerade aktive Site. Wer
    /// Standorte importiert, meint fast immer die Site, in der er arbeitet.</param>
    public PlaceSourceDialog(IReadOnlyList<PlaceSource> sources,
        IReadOnlyList<string> knownSites, string? preselect)
    {
        AvaloniaXamlLoader.Load(this);

        var box = this.FindControl<ComboBox>("SourceBox")!;
        foreach (var s in sources) box.Items.Add(new Entry(s));
        if (box.ItemCount > 0) box.SelectedIndex = 0;

        var list = this.FindControl<ItemsControl>("SiteList")!;
        list.ItemsSource = knownSites;

        // Vorauswahl erst setzen, wenn die Container erzeugt sind.
        if (!string.IsNullOrWhiteSpace(preselect))
            list.Loaded += (_, _) =>
            {
                foreach (var cb in list.GetVisualDescendants().OfType<CheckBox>())
                    if (string.Equals(cb.Content as string, preselect, System.StringComparison.OrdinalIgnoreCase))
                        cb.IsChecked = true;
            };
    }

    // Parameterloser ctor fuer XAML-Designer.
    public PlaceSourceDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("SourceBox")!.SelectedItem is not Entry entry)
        {
            Close(null);
            return;
        }

        var sites = this.FindControl<ItemsControl>("SiteList")!
            .GetVisualDescendants().OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Content as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        Close(new PlaceSourceChoice(entry.Source, sites));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
