using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

public sealed record CommentInputResult(string Comment, bool Persistent);

public partial class CommentInputDialog : ChromeWindow
{
    public CommentInputDialog(string target)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("TargetText")!.Text = target;
        Opened += (_, _) => this.FindControl<TextBox>("CommentBox")!.Focus();
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public CommentInputDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var text = this.FindControl<TextBox>("CommentBox")!.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;
        var persistent = this.FindControl<CheckBox>("PersistentCheck")!.IsChecked ?? false;
        Close(new CommentInputResult(text, persistent));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
