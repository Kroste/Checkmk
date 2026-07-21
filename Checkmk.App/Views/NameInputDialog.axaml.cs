using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

/// <summary>Kleiner Ein-Feld-Dialog: gibt den eingegebenen String zurueck (null = Abbruch).</summary>
public partial class NameInputDialog : ChromeWindow
{
    public NameInputDialog(string title, string prompt, string defaultValue = "")
    {
        AvaloniaXamlLoader.Load(this);
        Title = title;
        this.FindControl<Controls.TitleBar>("AppTitleBar")!.Title = title;
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;
        var box = this.FindControl<TextBox>("ValueBox")!;
        box.Text = defaultValue;
        box.SelectAll();
        Opened += (_, _) => box.Focus();
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public NameInputDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var text = this.FindControl<TextBox>("ValueBox")!.Text?.Trim();
        Close(string.IsNullOrEmpty(text) ? null : text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
