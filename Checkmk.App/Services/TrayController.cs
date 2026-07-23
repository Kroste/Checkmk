using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private NativeMenuItem _snoozeStatusItem = null!;
    private string? _lastFilterName;
    private bool _restoreInProgress;

    public bool IsMinimizedToTray { get; private set; }

    /// <summary>Wenn gesetzt: bis dahin keine Notifications ausgeben. Aendert nichts
    /// am Tray-Icon (der Ampelstatus bleibt sichtbar).</summary>
    public DateTimeOffset? SnoozedUntil { get; private set; }

    public TrayController(Application app, Window window, StatusViewModel status, IToastNotifier toast)
    {
        _app = app;
        _window = window;
        _status = status;
        _toast = toast;

        // Tray-Icons zur Laufzeit rendern: App-Icon + farbiger Status-Dot unten
        // rechts. Damit bleibt im Tray erkennbar dass das der Checkmk Cockpit
        // ist, und der Status ist trotzdem auf einen Blick sichtbar.
        _iconOk = TrayIconFactory.Create(TrayIconFactory.OkGreen);
        _iconWarn = TrayIconFactory.Create(TrayIconFactory.WarnYellow);
        _iconCrit = TrayIconFactory.Create(TrayIconFactory.CritRed);
        _iconUnknown = TrayIconFactory.Create(TrayIconFactory.UnknownGrey);

        BuildTray();

        _status.Refreshed += OnStatusRefreshed;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

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
        var test = new NativeMenuItem("Test-Benachrichtigung");
        test.Click += (_, _) =>
        {
            Log.Info("Test-Benachrichtigung ausgeloest ueber Tray-Menue.");
            _toast.Notify("Checkmk Cockpit — Test",
                "Wenn du diese Nachricht siehst, funktionieren Toasts. Zeitpunkt: "
                + DateTime.Now.ToString("HH:mm:ss"));
        };
        var snooze30 = new NativeMenuItem("Snooze 30 Min");
        snooze30.Click += (_, _) => Snooze(TimeSpan.FromMinutes(30));
        var snooze2h = new NativeMenuItem("Snooze 2 Std");
        snooze2h.Click += (_, _) => Snooze(TimeSpan.FromHours(2));
        var snoozeMorning = new NativeMenuItem("Snooze bis morgen 06:00");
        snoozeMorning.Click += (_, _) => Snooze(NextMorningSix() - DateTimeOffset.Now);
        _snoozeStatusItem = new NativeMenuItem("Snooze aufheben") { IsVisible = false };
        _snoozeStatusItem.Click += (_, _) => CancelSnooze();

        var exit = new NativeMenuItem("Beenden");
        exit.Click += (_, _) => (_app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        _trayIcon.Menu.Items.Add(show);
        _trayIcon.Menu.Items.Add(test);
        _trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Items.Add(snooze30);
        _trayIcon.Menu.Items.Add(snooze2h);
        _trayIcon.Menu.Items.Add(snoozeMorning);
        _trayIcon.Menu.Items.Add(_snoozeStatusItem);
        _trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Items.Add(exit);
        _trayIcon.Clicked += (_, _) => Restore();

        TrayIcon.SetIcons(_app, new TrayIcons { _trayIcon });
    }

    private void Snooze(TimeSpan duration)
    {
        SnoozedUntil = DateTimeOffset.Now.Add(duration);
        _snoozeStatusItem.Header = $"Snooze aufheben (aktiv bis {SnoozedUntil:HH:mm})";
        _snoozeStatusItem.IsVisible = true;
        Log.Info("Notifications ge-snoozed bis {Until}.", SnoozedUntil);
    }

    private void CancelSnooze()
    {
        SnoozedUntil = null;
        _snoozeStatusItem.IsVisible = false;
        Log.Info("Snooze manuell aufgehoben.");
    }

    private static DateTimeOffset NextMorningSix()
    {
        var now = DateTimeOffset.Now;
        var six = new DateTimeOffset(now.Year, now.Month, now.Day, 6, 0, 0, now.Offset);
        return now.Hour < 6 ? six : six.AddDays(1);
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

        // Snooze abgelaufen -> stillschweigend aufraeumen.
        if (SnoozedUntil is { } until && until <= DateTimeOffset.Now)
            CancelSnooze();

        var change = _monitor.Diff(services);
        if (change.HasChanges)
        {
            if (SnoozedUntil is not null)
            {
                Log.Debug("Statusaenderung erkannt — aber Snooze aktiv bis {Until}, kein Toast.", SnoozedUntil);
            }
            else if (IsMinimizedToTray)
            {
                Log.Info("Statusaenderung erkannt (CRIT {C}, WARN {W}, OK {O}, UNK {U}) — sende Toast.",
                    change.NewProblems, change.OtherChanges, change.Recoveries, 0);
                var title = $"Checkmk: {scope}";
                var body = change.ToText();
                if (change.FirstExample is { } ex)
                    body += $"\n{ex}";
                _toast.Notify(title, body);
            }
            else
            {
                Log.Debug("Statusaenderung erkannt — aber Fenster ist nicht ins Tray minimiert, kein Toast.");
            }
        }
    }
}
