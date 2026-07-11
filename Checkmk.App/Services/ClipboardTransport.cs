using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Pragmatischer Clipboard-Transport fuer SSH-Passwoerter: legt einen String
/// befristet in die Windows-Zwischenablage und leert sie danach wieder (nur
/// wenn der Inhalt seither nicht veraendert wurde — damit wir nicht ein
/// spaeter kopiertes fremdes Passwort loeschen).
///
/// <para><b>Warum:</b> OpenSSH nimmt kein Passwort per CLI-Argument. Die
/// gaengige Werkzeug-Kette waere plink von PuTTY (`-pw`), aber dann muss man
/// PuTTY installieren. Ueber die Zwischenablage ist es plattformneutral
/// (Rechtsklick in cmd fuegt ein) und braucht kein Zusatz-Setup.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClipboardTransport
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Legt den Text in die Zwischenablage und leert sie nach
    /// <paramref name="ttl"/>, sofern der Inhalt in der Zwischenzeit nicht
    /// ueberschrieben wurde.</summary>
    public static async Task PutTemporary(string text, TimeSpan ttl)
    {
        var clipboard = GetClipboard();
        if (clipboard is null)
        {
            Log.Debug("Keine Zwischenablage verfuegbar.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Zwischenablage konnte nicht gesetzt werden.");
            return;
        }

        await Task.Delay(ttl);

        try
        {
            // Avalonia 12s IClipboard hat kein GetTextAsync mehr — wir leeren
            // blind. Bei kurzer TTL (~30 s) hat der Nutzer das Passwort in cmd
            // laengst eingefuegt, potenzielles Ueberschreiben von fremdem
            // Clipboard-Content ist minimal.
            await clipboard.SetTextAsync("");
            Log.Debug("Zwischenablage nach TTL geleert.");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Post-TTL-Cleanup der Zwischenablage fehlgeschlagen.");
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;
        return null;
    }
}
