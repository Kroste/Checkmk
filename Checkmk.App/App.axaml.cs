using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App;

public partial class App : Application
{
    /// <summary>Wird in Program.Main gesetzt, bevor Avalonia startet.</summary>
    public static IServiceProvider Services { get; set; } = default!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Services.GetRequiredService<MainWindow>();
            var vm = Services.GetRequiredService<MainWindowViewModel>();

            vm.OpenSettingsRequested += async (_, _) =>
            {
                var settings = Services.GetRequiredService<SettingsWindow>();
                await settings.ShowDialog(window);
                await vm.ReconnectAsync();
            };

            vm.OpenAboutRequested += async (_, _) =>
            {
                var about = Services.GetRequiredService<AboutWindow>();
                await about.ShowDialog(window);
            };

            vm.OpenUpdateRequested += async (_, info) =>
            {
                var dialog = new UpdateDialog(info);
                var result = await dialog.ShowDialog<UpdateDialogResult>(window);
                if (result == UpdateDialogResult.Skip)
                    vm.SkipCurrentUpdate();
            };

            // Dashboard-Kachel-Klick: Filter aktivieren + Tab-Wechsel zu Status.
            vm.Dashboard.TileClicked += (_, filter) =>
            {
                vm.Status.Filters.Active = filter;
                window.SelectMainTab(0);
            };

            window.DataContext = vm;
            window.Opened += async (_, _) => await vm.InitializeAsync();
            desktop.MainWindow = window;

            // Tray-Icon, Minimieren ins Tray, Status-Notifications.
            var status = Services.GetRequiredService<StatusViewModel>();
            var toast = Services.GetRequiredService<IToastNotifier>();
            _trayController = new TrayController(this, window, status, toast);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Referenz halten, damit TrayController/TrayIcon nicht vom GC eingesammelt werden.
    private TrayController? _trayController;
}
