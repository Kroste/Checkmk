using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App.Views;

public partial class FilterManagerWindow : ChromeWindow
{
    public FilterManagerWindow(HostFilterCollection filters)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new FilterManagerViewModel(filters);
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public FilterManagerWindow() => AvaloniaXamlLoader.Load(this);

    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnManageTeamsClick(object? sender, RoutedEventArgs e)
    {
        // Ohne Datenbank gibt es keine Teams — der Knopf ist dann gar nicht
        // sichtbar, aber der Guard bleibt: der DI-Container liefert hier null.
        if (App.Services?.GetService<ITeamStore>() is not { } teams) return;

        var dialog = new TeamManagerWindow(teams);
        var changed = await dialog.ShowDialog<bool>(this);

        // Ein umbenanntes oder neues Team muss sofort in der Auswahl „Gehört zu"
        // stehen — sonst muesste man den Filter-Manager schliessen und neu
        // oeffnen, nur um den Filter zuordnen zu koennen.
        if (changed && DataContext is FilterManagerViewModel vm)
            vm.RefreshOwners();
    }
}
