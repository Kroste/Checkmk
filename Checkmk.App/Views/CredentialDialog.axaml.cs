using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

public sealed record CredentialResult(string User, string Password);

public partial class CredentialDialog : ChromeWindow
{
    public CredentialDialog(string target)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("TargetText")!.Text = $"Zielhost: {target}";
    }

    public CredentialDialog() => AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var user = this.FindControl<TextBox>("UserBox")!.Text ?? "";
        var pass = this.FindControl<TextBox>("PassBox")!.Text ?? "";
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(pass))
            return;
        Close(new CredentialResult(user.Trim(), pass));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
