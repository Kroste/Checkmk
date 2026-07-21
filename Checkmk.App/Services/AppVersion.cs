using System.Reflection;

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
