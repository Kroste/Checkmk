using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Models;

namespace Checkmk.App.Views;

public partial class FavoritePickerDialog : ChromeWindow
{
    public FavoritePickerDialog(string prompt, IEnumerable<HostFilter> favorites)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;
        var box = this.FindControl<ComboBox>("FavoriteBox")!;
        foreach (var f in favorites)
            box.Items.Add(f);
    }

    // Parameterloser ctor fuer XAML-Designer.
    public FavoritePickerDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
        => Close(this.FindControl<ComboBox>("FavoriteBox")!.SelectedItem as HostFilter);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
