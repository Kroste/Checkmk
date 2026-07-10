using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NLog;

#if WINDOWS10_0_19041_0_OR_GREATER
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
#endif

namespace Checkmk.App.Services;

/// <summary>Zeigt eine native OS-Benachrichtigung (Toast) an.</summary>
public interface IToastNotifier
{
    void Notify(string title, string body);
}

public static class ToastNotifierFactory
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static IToastNotifier Create()
    {
        // Zentrale Diagnose — wir haben schon mal ein "NullToastNotifier"-Log
        // bekommen, ohne dass klar war was daran schuld ist. Deshalb JETZT immer
        // beide Achsen loggen: was der Runtime denkt (OS) UND was compile-time
        // an Code drin ist (Windows-TFM ja/nein).
        var runtimeOs = RuntimeInformation.OSDescription;
        var fx = RuntimeInformation.FrameworkDescription;
        var isWin = OperatingSystem.IsWindows();
        var isLinux = OperatingSystem.IsLinux();
#if WINDOWS10_0_19041_0_OR_GREATER
        const string compileTfm = "net10.0-windows10.0.19041.0 (WinRT-Toast-Code IST im Binary)";
        const bool winRtCompiled = true;
#else
        const string compileTfm = "net10.0 (WinRT-Toast-Code NICHT im Binary)";
        const bool winRtCompiled = false;
#endif
        Log.Info("ToastNotifier-Diagnose: Runtime='{Os}' Framework='{Fx}' CompileTFM='{Tfm}' IsWindows={IsWindows} IsLinux={IsLinux}",
            runtimeOs, fx, compileTfm, isWin, isLinux);

#if WINDOWS10_0_19041_0_OR_GREATER
        if (isWin)
        {
            Log.Info("ToastNotifier gewaehlt: WindowsToastNotifier.");
            return new WindowsToastNotifier();
        }
#endif
        if (isWin && !winRtCompiled)
        {
            Log.Error("ToastNotifier gewaehlt: NullToastNotifier — auf Windows, aber Binary wurde OHNE WinRT-TFM gebaut. "
                    + "Bitte mit '-f net10.0-windows10.0.19041.0' publishen; die publish-win-x64-Task in .vscode/tasks.json hat das schon drin.");
            return new NullToastNotifier();
        }
        if (isLinux)
        {
            Log.Info("ToastNotifier gewaehlt: LinuxToastNotifier (notify-send).");
            return new LinuxToastNotifier();
        }
        Log.Warn("ToastNotifier gewaehlt: NullToastNotifier — unbekanntes OS (Runtime meldet '{Os}'). Weder Windows noch Linux erkannt.",
            runtimeOs);
        return new NullToastNotifier();
    }
}

/// <summary>Kein OS-Support -> stiller Fallback (Tray-Signal bleibt).</summary>
public sealed class NullToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void Notify(string title, string body)
        => Log.Warn("NullToastNotifier.Notify aufgerufen — keine Toast-Ausgabe (Title={Title}).", title);
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
///
/// <para><b>Registrierungs-Choreografie:</b> Wir setzen die AppUserModelID
/// <em>nicht</em> manuell. Der Toolkit-Wrapper macht das selbst — er leitet
/// die AumID aus dem Prozesspfad ab, legt beim ersten Toast einen
/// Startmenu-Shortcut mit exakt dieser AumID an und registriert einen
/// COM-Server dazu. Anhand des Startmenu-Shortcuts erlaubt Windows dann
/// Toasts fuer die AumID. Manuelles Setzen einer eigenen AumID (via
/// <c>SetCurrentProcessExplicitAppUserModelID</c>) hat vorher genau dieses
/// Match gebrochen — die Toasts trugen dann eine AumID, fuer die kein
/// Shortcut existierte, Windows hat sie silent verworfen.</para>
///
/// <para><b>Erzwungene Registrierung:</b> Ein OnActivated-Handler beim
/// Ctor triggert den Toolkit-Auto-Registrierer sofort, sonst laeuft er
/// lazy beim ersten Show()-Call — was bei self-contained Single-File-
/// Publish schon mal fehlgeschlagen ist.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public WindowsToastNotifier()
    {
        try
        {
            // Anhaengen eines Handlers erzwingt die Toolkit-Registrierung:
            // Startmenu-Shortcut mit AumID + COM-Server-Eintraege in HKCU.
            // Der Handler selbst muss nichts tun — wir reagieren nicht auf
            // Klicks im Toast.
            ToastNotificationManagerCompat.OnActivated += args =>
                Log.Debug("Toast-Activation empfangen: {Args}", args?.Argument);
            Log.Info("ToastNotificationManagerCompat registriert (AumID/Startmenu/COM-Server).");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ToastNotificationManagerCompat-Registrierung fehlgeschlagen — Toasts kommen evtl. nicht durch.");
        }
    }

    public void Notify(string title, string body)
    {
        Log.Info("Toast wird angezeigt: {Title} / {Body}", title, body);
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddHours(1);
                });
            Log.Info("Toast an Windows uebergeben (Titel: {Title}).", title);

            // Extra-Diagnose: Windows-eigene Sicht auf die letzten Notifications.
            // Wenn hier "Failed=X" auftaucht, verwirft Windows aktiv (z. B. wegen
            // Focus Assist, deaktivierte Kanal-Notifications, oder AumID-Mismatch).
            LogNotifierState();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Toast-Notification fehlgeschlagen (Title={Title}).", title);
        }
    }

    private static void LogNotifierState()
    {
        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            Log.Info("Notifier-Setting: {Setting} (Enabled=Toasts erlaubt; DisabledForApplication=Nutzer hat's fuer die App aus; DisabledByGroupPolicy/User; DisabledByManifest).",
                notifier.Setting);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CreateToastNotifier() zur Diagnose fehlgeschlagen.");
        }
    }
}

#endif
