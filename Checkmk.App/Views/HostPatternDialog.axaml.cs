using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

/// <summary>
/// Host-Namensmuster eines Bereichs, mit <b>Live-Vorschau der Treffer</b>.
///
/// Die Vorschau ist der Punkt: Ein regulärer Ausdruck ist für die meisten
/// unlesbar, aber „diese 7 Hosts würden zugeordnet" versteht jeder sofort.
/// Ohne sie müsste man ein Muster speichern, Vorschläge erzeugen und wieder
/// zurückgehen, um zu sehen, ob es stimmt.
/// </summary>
public partial class HostPatternDialog : ChromeWindow
{
    private readonly IReadOnlyList<string> _hosts = [];

    public HostPatternDialog(string areaName, string? pattern, IReadOnlyList<string> knownHosts)
    {
        AvaloniaXamlLoader.Load(this);

        _hosts = knownHosts;

        Title = $"Host-Muster: {areaName}";
        this.FindControl<TitleBar>("DialogTitleBar")!.Title = Title;
        this.FindControl<TextBlock>("PromptText")!.Text =
            $"Welche Hosts gehören zu „{areaName}“?";

        var box = this.FindControl<TextBox>("PatternBox")!;
        box.Text = pattern ?? "";
        box.TextChanged += (_, _) => UpdatePreview();

        UpdatePreview();
    }

    // Parameterloser ctor fuer XAML-Designer.
    public HostPatternDialog() => AvaloniaXamlLoader.Load(this);

    private void UpdatePreview()
    {
        var pattern = this.FindControl<TextBox>("PatternBox")!.Text ?? "";
        var state = this.FindControl<TextBlock>("StateText")!;
        var list = this.FindControl<ListBox>("MatchList")!;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            state.Text = "Kein Muster — dieser Bereich macht keine Vorschläge.";
            state.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            list.ItemsSource = System.Array.Empty<string>();
            return;
        }

        if (!HostPatternMatcher.IsValid(pattern))
        {
            // Klare Rueckmeldung statt stiller Wirkungslosigkeit: Ein kaputtes
            // Muster trifft sonst einfach nichts und man sucht den Fehler beim
            // Hostnamen.
            state.Text = "Ungültiger Ausdruck — trifft nichts.";
            state.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            list.ItemsSource = System.Array.Empty<string>();
            return;
        }

        var matches = _hosts.Where(h => HostPatternMatcher.Matches(pattern, h))
            .OrderBy(h => h, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        state.Text = matches.Count == 0
            ? $"Kein Treffer unter {_hosts.Count} bekannten Hosts."
            : $"{matches.Count} von {_hosts.Count} Hosts treffen zu.";
        state.Foreground = new SolidColorBrush(matches.Count == 0
            ? Color.FromRgb(0xFF, 0xCA, 0x28)
            : Color.FromRgb(0x66, 0xBB, 0x6A));
        list.ItemsSource = matches;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => Close("");

    private void OnOkClick(object? sender, RoutedEventArgs e)
        => Close(this.FindControl<TextBox>("PatternBox")!.Text?.Trim() ?? "");

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
