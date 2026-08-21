using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

/// <summary>
/// Ergebnis der Bereichsauswahl. <see cref="AreaId"/> ist <c>null</c>, wenn die
/// Zuordnung entfernt werden soll — deshalb ein eigener Typ und nicht einfach
/// ein <c>int?</c>: „abgebrochen" und „auf keinen Bereich setzen" sind zwei
/// verschiedene Dinge und wurden sonst zwangsläufig verwechselt.
/// </summary>
public sealed record AreaPickResult(int? AreaId);

public partial class AreaPickerDialog : ChromeWindow
{
    /// <summary>Eintrag der Liste — zeigt die Verschachtelung als Einrückung,
    /// damit „Bereich A" unter Campus und unter Stadthaus unterscheidbar bleibt.</summary>
    private sealed record Entry(int AreaId, string Label)
    {
        public override string ToString() => Label;
    }

    public AreaPickerDialog(string prompt, IEnumerable<AreaNodeViewModel> roots)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;

        var box = this.FindControl<ComboBox>("AreaBox")!;
        foreach (var e in Flatten(roots, 0))
            box.Items.Add(e);
    }

    // Parameterloser ctor fuer XAML-Designer.
    public AreaPickerDialog() => AvaloniaXamlLoader.Load(this);

    private static IEnumerable<Entry> Flatten(IEnumerable<AreaNodeViewModel> nodes, int depth)
    {
        foreach (var n in nodes)
        {
            if (n.IsUnassigned) continue;   // kein gueltiges Zuweisungsziel
            yield return new Entry(n.AreaId, new string(' ', depth * 4) + n.Name);
            foreach (var child in Flatten(n.Children, depth + 1))
                yield return child;
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("AreaBox")!.SelectedItem is not Entry entry) return;
        Close(new AreaPickResult(entry.AreaId));
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
        => Close(new AreaPickResult(null));

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
