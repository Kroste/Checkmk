using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

/// <summary>
/// In welchen Checkmk-Sites ein Bereich sichtbar ist.
///
/// Ohne diesen Dialog ließ sich die Sichtbarkeit gar nicht ändern — der Store
/// konnte es, die Oberfläche bot es nicht an. Folge: Von Hand angelegte
/// Bereiche wie „Container" standen mit in der Schul-Sicht, und die Korrektur
/// ging nur über SQL.
/// </summary>
public partial class SiteSelectDialog : ChromeWindow
{
    /// <summary>Leere Liste = überall sichtbar. Abbruch liefert <c>null</c> —
    /// „überall" und „abgebrochen" sind zwei verschiedene Dinge.</summary>
    public SiteSelectDialog(string areaName, IReadOnlyList<string> knownSites,
        IReadOnlyList<string> current)
    {
        AvaloniaXamlLoader.Load(this);

        Title = $"Sichtbarkeit: {areaName}";
        this.FindControl<TitleBar>("DialogTitleBar")!.Title = Title;
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"In welchen Sites soll „{areaName}“ erscheinen?";

        var list = this.FindControl<ItemsControl>("SiteList")!;

        // Auch Sites anzeigen, die zwar zugeordnet, aber nicht (mehr) in den
        // Verbindungseinstellungen stehen — sonst verschwaende eine Zuordnung
        // stillschweigend beim Speichern.
        var all = knownSites.Concat(current)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        list.ItemsSource = all;

        list.Loaded += (_, _) =>
        {
            foreach (var cb in list.GetVisualDescendants().OfType<CheckBox>())
                if (cb.Content is string s
                    && current.Contains(s, StringComparer.OrdinalIgnoreCase))
                    cb.IsChecked = true;
        };
    }

    // Parameterloser ctor fuer XAML-Designer.
    public SiteSelectDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> chosen = [.. this.FindControl<ItemsControl>("SiteList")!
            .GetVisualDescendants().OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Content as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)];

        Close(chosen);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
