using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

public partial class PlaceSourceDialog : ChromeWindow
{
    /// <summary>Zeigt das Label statt der Kennung — <c>PlaceSource</c> ist ein
    /// Record, dessen ToString sonst alle Felder ausschreibt.</summary>
    private sealed record Entry(PlaceSource Source)
    {
        public override string ToString() => Source.Label;
    }

    public PlaceSourceDialog(IReadOnlyList<PlaceSource> sources)
    {
        AvaloniaXamlLoader.Load(this);

        var box = this.FindControl<ComboBox>("SourceBox")!;
        foreach (var s in sources) box.Items.Add(new Entry(s));
        if (box.ItemCount > 0) box.SelectedIndex = 0;
    }

    // Parameterloser ctor fuer XAML-Designer.
    public PlaceSourceDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
        => Close((this.FindControl<ComboBox>("SourceBox")!.SelectedItem as Entry)?.Source);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
