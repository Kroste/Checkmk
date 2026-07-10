using System.Diagnostics;
using System.Runtime.Versioning;
using NLog;

#if WINDOWS10_0_19041_0_OR_GREATER
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace Checkmk.App.Services;

/// <summary>Zeigt eine native OS-Benachrichtigung (Toast) an.</summary>
public interface IToastNotifier
{
    void Notify(string title, string body);
}

public static class ToastNotifierFactory
{
    public static IToastNotifier Create()
    {
#if WINDOWS10_0_19041_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return new WindowsToastNotifier();
#endif
        if (OperatingSystem.IsLinux())
            return new LinuxToastNotifier();
        return new NullToastNotifier();
    }
}

/// <summary>Kein OS-Support -> stiller Fallback (Tray-Signal bleibt).</summary>
public sealed class NullToastNotifier : IToastNotifier
{
    public void Notify(string title, string body) { }
}

/// <summary>Linux: nutzt das Standard-CLI notify-send (KDE/GNOME, Bazzite vorhanden).</summary>
public sealed class LinuxToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void Notify(string title, string body)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                ArgumentList = { "-a", "Checkmk Cockpit", title, body },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "notify-send nicht verfuegbar.");
        }
    }
}

#if WINDOWS10_0_19041_0_OR_GREATER

/// <summary>
/// Windows: WinRT-Toast-Notification via <see cref="ToastContentBuilder"/>.
/// Erscheint modern im Action Center (Windows 10/11), ueberlebt kurzzeitiges
/// Aufblitzen der Popup-Version.
///
/// <para><b>Warum nicht Shell_NotifyIcon:</b> Balloons an ein per <c>NIS_HIDDEN</c>
/// registriertes Tray-Icon werden von Vista+ in eine Queue gelegt, die nur beim
/// Sichtbarwerden des Icons ausgeliefert wird — was in dieser App nie passierte.
/// Ergebnis: keine Notifications sichtbar.</para>
///
/// <para><b>Warum Toolkit statt direktem WinRT:</b> <c>ToastNotificationManagerCompat</c>
/// registriert beim ersten Aufruf einen Startmenu-Shortcut mit AppUserModelID —
/// erst dadurch behaelt Windows unpackaged Toasts im Action Center statt sie
/// direkt zu verwerfen. Einmalig sichtbarer Nebeneffekt: „Checkmk Cockpit"
/// erscheint im Startmenu.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void Notify(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show(toast =>
                {
                    // Aeltere Snapshots verschwinden aus dem Action Center — sonst haeuft
                    // sich bei Auto-Refresh alle N Minuten eine Historie auf.
                    toast.ExpirationTime = DateTimeOffset.Now.AddHours(1);
                });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Toast-Notification fehlgeschlagen (Title={Title}).", title);
        }
    }
}

#endif
