using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Checkmk.App.ViewModels;
using Checkmk.Core.Models;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Verwaltet das Tray-Icon: Minimieren ins Tray, Ampelfarbe + Tooltip fuer den
/// aktiven Filter, und Toast-Benachrichtigung bei Statusaenderungen (nur wenn
/// ins Tray minimiert). Aktiviert beim Minimieren automatisch die Auto-Aktualisierung.
/// </summary>
public sealed class TrayController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Application _app;
    private readonly Window _window;
    private readonly StatusViewModel _status;
    private readonly IToastNotifier _toast;
    private readonly StatusChangeMonitor _monitor = new();

    private readonly WindowIcon _iconOk;
    private readonly WindowIcon _iconWarn;
    private readonly WindowIcon _iconCrit;
    private readonly WindowIcon _iconUnknown;

    private TrayIcon _trayIcon = null!;
    private string? _lastFilterName;
    private bool _restoreInProgress;

    public bool IsMinimizedToTray { get; private set; }

    public TrayController(Application app, Window window, StatusViewModel status, IToastNotifier toast)
    {
        _app = app;
        _window = window;
        _status = status;
        _toast = toast;

        _iconOk = Load("ok");
        _iconWarn = Load("warn");
        _iconCrit = Load("crit");
        _iconUnknown = Load("unknown");

        BuildTray();

        _status.Refreshed += OnStatusRefreshed;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    private static WindowIcon Load(string name)
        => new(AssetLoader.Open(new Uri($"avares://Checkmk.App/Assets/tray/{name}.ico")));

    private void BuildTray()
    {
        _trayIcon = new TrayIcon
        {
            Icon = _iconOk,
            ToolTipText = "Checkmk Cockpit",
            IsVisible = true,
            Menu = new NativeMenu()
        };

        var show = new NativeMenuItem("Anzeigen");
        show.Click += (_, _) => Restore();
        var exit = new NativeMenuItem("Beenden");
        exit.Click += (_, _) => (_app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        _trayIcon.Menu.Items.Add(show);
        _trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Items.Add(exit);
        _trayIcon.Clicked += (_, _) => Restore();

        TrayIcon.SetIcons(_app, new TrayIcons { _trayIcon });
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty || _restoreInProgress)
            return;

        if ((WindowState)e.NewValue! == WindowState.Minimized)
            MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        IsMinimizedToTray = true;
        _window.Hide();
        // Auto-Refresh muss laufen, sonst gibt es keine Aenderungen zu melden.
        _status.AutoRefresh = true;
        Log.Debug("Ins Tray minimiert, Auto-Refresh aktiviert.");
    }

    private void Restore()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _restoreInProgress = true;
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
            IsMinimizedToTray = false;
            _restoreInProgress = false;
        });
    }

    private void OnStatusRefreshed(IReadOnlyList<ServiceStatus> services, string? filterName)
    {
        // Ampelfarbe + Tooltip
        var crit = services.Count(s => s.ServiceState == ServiceState.Critical);
        var warn = services.Count(s => s.ServiceState == ServiceState.Warning);
        var unknown = services.Count(s => s.ServiceState == ServiceState.Unknown);
        var ok = services.Count(s => s.ServiceState == ServiceState.Ok);

        _trayIcon.Icon = crit > 0 ? _iconCrit
            : warn > 0 ? _iconWarn
            : unknown > 0 ? _iconUnknown
            : _iconOk;

        var scope = string.IsNullOrWhiteSpace(filterName) ? "Alle Hosts" : filterName;
        _trayIcon.ToolTipText = $"Checkmk Cockpit — {scope}\nCRIT {crit} · WARN {warn} · OK {ok}";

        // Bei Filterwechsel Monitor zuruecksetzen (kein Fehlalarm durch anderen Datensatz).
        if (filterName != _lastFilterName)
        {
            _monitor.Reset();
            _lastFilterName = filterName;
        }

        var change = _monitor.Diff(services);
        if (change.HasChanges && IsMinimizedToTray)
        {
            var title = $"Checkmk: {scope}";
            var body = change.ToText();
            if (change.FirstExample is { } ex)
                body += $"\n{ex}";
            _toast.Notify(title, body);
        }
    }
}
