using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Checkmk.App.ViewModels;

public enum ServiceActionMode
{
    Acknowledge,
    Downtime
}

/// <summary>
/// Kleiner Dialog fuer "Acknowledge" bzw. "Downtime setzen" auf einem Service.
/// Kommentar ist in beiden Faellen Pflicht (Checkmk-Anforderung).
/// </summary>
public sealed partial class ServiceActionDialogViewModel : ObservableObject
{
    public ServiceActionMode Mode { get; }
    public string HostName { get; }
    public string ServiceDescription { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private string _comment = "";

    [ObservableProperty]
    private DowntimePreset _selectedPreset;

    public bool IsDowntime => Mode == ServiceActionMode.Downtime;
    public string Header => IsDowntime ? "Downtime setzen" : "Problem acknowledgen";
    public string Target => $"{HostName} / {ServiceDescription}";
    public IReadOnlyList<DowntimePreset> Presets => DowntimePreset.Defaults;

    /// <summary>Wird mit true (OK) oder false (Abbrechen) ausgeloest.</summary>
    public event EventHandler<bool>? RequestClose;

    public ServiceActionDialogViewModel(ServiceActionMode mode, string hostName, string serviceDescription)
    {
        Mode = mode;
        HostName = hostName;
        ServiceDescription = serviceDescription;
        _selectedPreset = DowntimePreset.Defaults[0];
    }

    /// <summary>Berechnetes Zeitfenster fuer die Downtime (ab jetzt).</summary>
    public (DateTimeOffset Start, DateTimeOffset End) Window()
    {
        var now = DateTimeOffset.Now;
        return (now, SelectedPreset.EndFrom(now));
    }

    private bool CanConfirm => !string.IsNullOrWhiteSpace(Comment);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Ok() => RequestClose?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);
}

/// <summary>Vordefinierte Downtime-Dauer (ab jetzt).</summary>
public sealed class DowntimePreset(string label, Func<DateTimeOffset, DateTimeOffset> endFrom)
{
    public string Label { get; } = label;
    public Func<DateTimeOffset, DateTimeOffset> EndFrom { get; } = endFrom;

    public override string ToString() => Label;

    public static IReadOnlyList<DowntimePreset> Defaults { get; } =
    [
        new DowntimePreset("1 Stunde", n => n.AddHours(1)),
        new DowntimePreset("2 Stunden", n => n.AddHours(2)),
        new DowntimePreset("4 Stunden", n => n.AddHours(4)),
        new DowntimePreset("Bis morgen 06:00", NextMorningSix)
    ];

    private static DateTimeOffset NextMorningSix(DateTimeOffset from)
    {
        var six = new DateTimeOffset(from.Year, from.Month, from.Day, 6, 0, 0, from.Offset);
        return from.Hour < 6 ? six : six.AddDays(1);
    }
}
