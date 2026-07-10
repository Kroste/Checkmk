namespace Checkmk.Core;

/// <summary>Helfer zum Parsen von SemVer-artigen Git-Tags (mit optionalem <c>v</c>-Prefix
/// und MinVer-Suffixen).</summary>
public static class SemVerTag
{
    /// <summary>„v1.2.3" -> 1.2.3; „1.2.3+build.4" -> 1.2.3 (MinVer-Suffixe abschneiden).</summary>
    public static bool TryParse(string tag, out Version version)
    {
        var s = tag.TrimStart('v', 'V');
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out version!);
    }
}
