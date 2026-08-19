namespace Checkmk.App.Services;

/// <summary>
/// Loest den Pfad der Verbindungsdatei aus dem <c>bootstrap.json</c>-Eintrag auf.
/// <para>
/// Hintergrund: die Bootstrap-Datei wird <b>zentral geteilt</b>. Ein dort
/// eingetragener absoluter Pfad in ein Benutzerprofil (
/// <c>C:\Users\Meier\AppData\Roaming\…</c>) gilt damit fuer <b>alle</b> Nutzer —
/// und alle ausser Meier bekommen beim Speichern
/// <c>UnauthorizedAccessException</c> bzw. <c>DirectoryNotFoundException</c>.
/// Genau so ist der Pfad einmal in die zentrale Datei gewandert: eine frueher
/// aufgeloeste lokale Vorgabe wurde dorthin migriert.
/// </para>
/// <para>
/// Regeln, bewusst als reine Funktion (testbar, ohne Dateisystem):
/// leer =&gt; user-lokale Vorgabe · Umgebungsvariablen werden expandiert ·
/// ein Pfad im Profil eines <b>anderen</b> Nutzers wird verworfen.
/// </para>
/// </summary>
public static class SettingsPathResolver
{
    /// <param name="configured">Wert aus <c>bootstrap.json</c> (darf leer/null sein).</param>
    /// <param name="localDefault">User-lokale Vorgabe, z. B. unter %APPDATA%.</param>
    /// <param name="userProfile">Profilverzeichnis des angemeldeten Nutzers
    /// (<c>C:\Users\Meier</c>). Leer =&gt; die Fremdprofil-Pruefung entfaellt.</param>
    public static string Resolve(string? configured, string localDefault, string? userProfile)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return localDefault;

        var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());

        // Nach dem Expandieren kann immer noch eine unaufgeloeste Variable
        // dastehen (%FOO% ohne Definition) — als Pfad waere das Unsinn.
        if (expanded.Contains('%'))
            return localDefault;

        return PointsIntoForeignUserProfile(expanded, userProfile) ? localDefault : expanded;
    }

    /// <summary>
    /// Liegt <paramref name="path"/> im Benutzerprofil eines <b>anderen</b> Nutzers?
    /// UNC-Pfade und alles ausserhalb von <c>…\Users\</c> sind ausgenommen — ein
    /// Fileshare oder ein <c>D:\cockpit\settings.json</c> sind legitime Konfigurationen.
    /// </summary>
    public static bool PointsIntoForeignUserProfile(string path, string? userProfile)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(userProfile))
            return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;   // UNC = geteilte Ablage, absichtlich

        var usersRoot = Path.GetDirectoryName(userProfile.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(usersRoot))
            return false;

        // Unterhalb von C:\Users, aber nicht im eigenen Profil => fremdes Profil.
        return IsUnder(path, usersRoot) && !IsUnder(path, userProfile);
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
