using System.Reflection;
using Checkmk.Core;

namespace Checkmk.App.Services;

/// <summary>
/// Anzeige-Version der App. MinVer setzt die <c>AssemblyVersion</c> nur auf
/// Major.0.0.0 (z. B. 1.0.0.0 fuer Tag v1.6.0) — <c>GetName().Version</c> zeigt
/// daher immer "1.0.0.0". Die vollstaendige SemVer steckt im
/// <see cref="AssemblyInformationalVersionAttribute"/>; von dort nehmen wir sie,
/// ohne das Git-Metadaten-Suffix nach '+'.
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = Resolve();

    /// <summary>
    /// Laufende Version als <see cref="Version"/> — die <b>einzige</b> Quelle
    /// für jeden Versionsvergleich (Update-Check).
    ///
    /// <para><b>Nie <c>GetName().Version</c> dafür nehmen.</b> MinVer setzt die
    /// auf <c>Major.0.0.0</c>; ein Vergleich damit meldet ab Version 1.0.1
    /// dauerhaft ein Update — und bei einem Kanal mit älterem Paket sogar ein
    /// Downgrade. Genau so passiert: laufend 1.15.1, angeboten 1.14.0.</para>
    ///
    /// <para>Vorabversionen werden auf ihren Zahlenkern gekürzt
    /// (<c>1.15.1-alpha.0.2</c> → <c>1.15.1</c>). Damit gilt eine Alpha als
    /// ihre Zielversion; das fertige 1.15.1 wird ihr dann nicht mehr angeboten.
    /// Bewusst so — wer Alphas fährt, baut sie selbst.</para>
    /// </summary>
    public static Version Current { get; } = ResolveVersion();

    private static Version ResolveVersion()
    {
        if (SemVerTag.TryParse(Display, out var parsed)) return parsed;

        var asm = Assembly.GetExecutingAssembly().GetName().Version;
        return asm is null
            ? new Version(0, 0)
            : new Version(asm.Major, asm.Minor, Math.Max(0, asm.Build), Math.Max(0, asm.Revision));
    }

    private static string Resolve()
    {
        var asm = Assembly.GetExecutingAssembly();
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return asm.GetName().Version?.ToString() ?? "?";
    }
}
