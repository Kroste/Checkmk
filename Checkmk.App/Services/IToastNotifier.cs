using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// <para><b>Zwingend fuer unpackaged apps:</b> die AppUserModelID muss VOR dem
/// ersten Show() gesetzt sein (via <c>SetCurrentProcessExplicitAppUserModelID</c>) —
/// sonst verwirft Windows die Toasts <b>silent ohne Exception</b>. Bei
/// self-contained Single-File-Publish gibt es zusaetzlich das Problem, dass
/// der ToolkitCompat-Auto-Registrierer den Prozess-Pfad aus dem Bundle-Extract-
/// Cache zieht — auch da hilft eine explizite AppID.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Stabile, human-lesbare AppID. Muss zur Startmenue-Verknuepfung passen,
    // die ToastNotificationManagerCompat beim ersten Toast anlegt.
    private const string AppUserModelId = "Kroste.CheckmkCockpit";

    private bool _registrationAttempted;

    public WindowsToastNotifier()
    {
        TryRegister();
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
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Toast-Notification fehlgeschlagen (Title={Title}).", title);
        }
    }

    private void TryRegister()
    {
        if (_registrationAttempted) return;
        _registrationAttempted = true;
        try
        {
            // Muss VOR dem ersten Toast-Aufruf laufen — sonst verwirft Windows
            // die Toasts silent, weil kein aktives AppUserModelID existiert.
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            Log.Info("AppUserModelID gesetzt: {AppId}", AppUserModelId);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "AppUserModelID konnte nicht gesetzt werden — Toasts kommen evtl. nicht durch.");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);
}

#endif
