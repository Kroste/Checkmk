using Microsoft.Toolkit.Uwp.Notifications;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Zeigt eine native Windows-Toast-Notification an.</summary>
public interface IToastNotifier
{
    void Notify(string title, string body);
}

/// <summary>
/// WinRT-Toast-Notification via <see cref="ToastContentBuilder"/>.
///
/// <para><b>Registrierungs-Choreografie:</b> AumID + Startmenu-Shortcut +
/// COM-Server registriert der Toolkit-Wrapper selbst konsistent. Ein leerer
/// <c>OnActivated</c>-Handler im Ctor triggert die Registrierung sofort,
/// statt sie lazy beim ersten <c>Show()</c> laufen zu lassen — bei
/// self-contained Single-File-Publish war das schon mal fehlgeschlagen.</para>
///
/// <para><b>Diagnose:</b> Nach jedem <c>Show()</c> wird
/// <c>ToastNotifier.Setting</c> geloggt. Damit sagt Windows uns direkt, ob
/// z. B. der User Notifications fuer die App abgeschaltet hat oder eine
/// GPO blockt.</para>
/// </summary>
public sealed class WindowsToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public WindowsToastNotifier()
    {
        try
        {
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
                    // Aeltere Snapshots verschwinden aus dem Action Center — sonst haeuft
                    // sich bei Auto-Refresh eine Historie auf.
                    toast.ExpirationTime = DateTimeOffset.Now.AddHours(1);
                });
            Log.Info("Toast an Windows uebergeben (Titel: {Title}).", title);
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
            Log.Info("Notifier-Setting: {Setting} (Enabled=Toasts erlaubt; DisabledForApplication/User/GroupPolicy/Manifest = Windows blockt).",
                notifier.Setting);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CreateToastNotifier() zur Diagnose fehlgeschlagen.");
        }
    }
}
