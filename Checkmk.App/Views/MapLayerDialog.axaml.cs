using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

/// <summary>Ergebnis: <c>null</c> im <c>LayerName</c> heißt „Vorgabe".</summary>
public sealed record MapLayerChoice(string? LayerName);

/// <summary>
/// Kartenhintergrund je Bereich.
///
/// Der Anlass ist die Campus-Ebene: Auf einem Gelände mit mehreren Serverräumen
/// ist die Liegenschaftskarte oder ein Gebäudeplan brauchbar, auf der
/// Stadtübersicht wäre er unlesbar. Deshalb hängt die Wahl am Bereich und nicht
/// an der Zoomstufe.
/// </summary>
public partial class MapLayerDialog : ChromeWindow
{
    /// <summary>Steht für „kein eigener Hintergrund" — als Eintrag in der Liste,
    /// damit das Zurücksetzen derselbe Handgriff ist wie das Setzen.</summary>
    private const string DefaultEntry = "(Vorgabe)";

    public MapLayerDialog(string areaName, IReadOnlyList<string> layers, string? current)
    {
        AvaloniaXamlLoader.Load(this);

        Title = $"Kartenhintergrund: {areaName}";
        this.FindControl<TitleBar>("DialogTitleBar")!.Title = Title;
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"Welcher Hintergrund gilt für „{areaName}“?";

        var list = this.FindControl<ListBox>("LayerList")!;
        var items = new List<string> { DefaultEntry };
        items.AddRange(layers);
        list.ItemsSource = items;

        // Ein gespeicherter Name, den es in den Kartenquellen nicht mehr gibt,
        // faellt auf „Vorgabe" zurueck — sonst stuende die Auswahl leer da und
        // man wuesste nicht, ob etwas gesetzt ist.
        list.SelectedItem = current is not null
            && items.Any(i => i.Equals(current, StringComparison.OrdinalIgnoreCase))
            ? items.First(i => i.Equals(current, StringComparison.OrdinalIgnoreCase))
            : DefaultEntry;
    }

    // Parameterloser ctor fuer XAML-Designer.
    public MapLayerDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var chosen = this.FindControl<ListBox>("LayerList")!.SelectedItem as string;
        Close(new MapLayerChoice(chosen == DefaultEntry ? null : chosen));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
