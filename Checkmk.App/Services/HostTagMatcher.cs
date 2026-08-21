using Checkmk.Data;

namespace Checkmk.App.Services;

/// <summary>Ein Vorschlag „dieser Tag-Wert gehört zu diesem Bereich".</summary>
/// <param name="Conflicts">Weitere Bereiche, auf die derselbe Tag passt.
/// Nicht leer heißt: nicht eindeutig, nichts übernehmen.</param>
public sealed record TagMatch(
    string TagValue,
    int HostCount,
    int AreaId,
    string AreaName,
    string? CurrentTag,
    IReadOnlyList<string> Conflicts)
{
    public bool IsAmbiguous => Conflicts.Count > 0;

    /// <summary>Der Bereich trägt schon genau diesen Tag — nichts zu tun.</summary>
    public bool IsUnchanged => string.Equals(CurrentTag, TagValue, StringComparison.OrdinalIgnoreCase);

    public string Note => IsAmbiguous
        ? $"mehrdeutig — passt auch auf {string.Join(", ", Conflicts)}"
        : IsUnchanged
            ? "unverändert"
            : CurrentTag is null ? "neu" : $"ersetzt {CurrentTag}";
}

/// <summary>
/// Bringt die Checkmk-Ortstags mit den Bereichen zusammen — <b>einmal</b>, unter
/// Sichtkontrolle. Danach steht der Tag-Wert am Bereich und die Zuordnung ist
/// ein exakter Stringvergleich.
///
/// Warum dieser Umweg statt einer Ableitung zur Laufzeit: Die Übersetzung
/// Schulnummer → Tag ist unregelmäßig. Gemessen am 2026-08-21 auf
/// <c>schul_it</c>:
///
/// <list type="bullet">
/// <item><c>schule_46</c> ↔ SCHULNUM <c>46</c> — der Normalfall.</item>
/// <item><c>schule_01</c> ↔ SCHULNUM <c>1</c> — führende Null nur im Tag.</item>
/// <item><c>schule_2526</c> ↔ SCHULNUM <c>25/26</c> — beide Nummern aneinander.</item>
/// <item><c>schule_10</c> ↔ SCHULNUM <c>10/30</c> — hier nur die <i>erste</i>.</item>
/// </list>
///
/// Eine Regel, die alle vier Fälle zur Laufzeit trifft, träfe irgendwann auch
/// den falschen Bereich, und das fiele niemandem auf. Deshalb: raten, zeigen,
/// bestätigen, speichern.
///
/// <para><b>Das Präfix gehört zum Schlüssel.</b> Die Zahl allein zu vergleichen
/// war ein Fehler, den der Bestand sofort aufgedeckt hat: Fünf Hosts
/// (<c>25-SW01</c>, <c>NAS25-01</c>, …) tragen <c>tag_location_filiale =
/// filiale_04</c> und gehören zur Karl-Foerster-Schule. Über die nackte 4
/// landeten sie beim Hermann-von-Helmholtz-Gymnasium, das die Schulnummer 4
/// hat. <c>filiale_</c> und <c>schule_</c> sind zwei Nummernkreise, und die
/// dürfen sich nicht berühren.</para>
/// </summary>
public static class HostTagMatcher
{
    /// <summary>
    /// Tag-Präfix je Importquelle. Nur Bereiche aus einer Quelle mit bekanntem
    /// Präfix nehmen am automatischen Abgleich teil — für die
    /// Verwaltungsstandorte gibt es weder Code noch Ortstag, da hilft nur die
    /// Eingabe von Hand.
    /// </summary>
    private static readonly Dictionary<string, string> PrefixBySource = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LHP-Schulen"] = "schule",
    };

    /// <summary>Nummernkreis, in dem der Code eines Bereichs gilt.</summary>
    internal static string? PrefixFor(AreaRow area)
        => area.ExternalSource is { } s && PrefixBySource.TryGetValue(s, out var p) ? p : null;

    /// <summary>Nummernkreis eines Tag-Werts: alles vor dem letzten Unterstrich.</summary>
    internal static string? PrefixOfTag(string tagValue)
    {
        var i = tagValue.LastIndexOf('_');
        return i > 0 ? tagValue[..i] : null;
    }

    /// <summary>
    /// Die Zahl in einem Text, oder <c>null</c>. Bewusst <b>keine</b>
    /// Ziffern-Extraktion aus gemischtem Text: Kürzel wie <c>F26</c> (berufliche
    /// Schule) oder <c>OSZ III</c> würden sonst zu „26" und beanspruchten die
    /// Hosts der Schule 26. Genau daran ist ein erster Versuch gescheitert —
    /// vier Tag-Werte wurden mehrdeutig, weil <c>F21</c> und die 21 auf
    /// dieselbe Zahl fielen.
    /// </summary>
    internal static string? Number(string text)
    {
        var s = text.Trim();
        if (s.Length == 0 || !s.All(char.IsAsciiDigit)) return null;
        var trimmed = s.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>
    /// Die Zahlen, unter denen ein Bereich per Tag ansprechbar ist. Bei einem
    /// Doppelcode <c>25/26</c> sind das <c>2526</c> (die Schreibweise in
    /// <c>schule_2526</c>) <b>und</b> beide Hälften einzeln — Checkmk führt die
    /// 10/30 als <c>schule_10</c>.
    /// </summary>
    internal static IReadOnlyList<string> KeysFor(string? externalCode)
    {
        if (string.IsNullOrWhiteSpace(externalCode)) return [];

        var parts = externalCode.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Number)
            .ToList();

        // Enthaelt der Code irgendetwas, das keine reine Zahl ist, gibt es
        // nichts abzuleiten. Lieber kein Schluessel als ein falscher.
        if (parts.Count == 0 || parts.Any(p => p is null)) return [];

        var keys = new List<string>();
        if (parts.Count > 1) keys.Add(string.Concat(parts));
        keys.AddRange(parts!);
        return keys;
    }

    /// <summary>
    /// Zahl aus einem Tag-Wert. <c>schule_46</c> → <c>46</c>. Alles vor dem
    /// letzten Unterstrich gilt als Präfix; ohne Unterstrich zählt der ganze
    /// Wert. Ist der Rest keine reine Zahl, gibt es keinen Schlüssel — dann
    /// muss der Tag von Hand zugeordnet werden.
    /// </summary>
    internal static string? NumberOfTag(string tagValue)
    {
        var i = tagValue.LastIndexOf('_');
        var tail = i >= 0 ? tagValue[(i + 1)..] : tagValue;
        return Number(tail);
    }

    /// <summary>
    /// Schlägt für jeden vorkommenden Tag-Wert den passenden Bereich vor.
    /// Bereiche ohne Zahl im <c>ExternalCode</c> und Tags ohne Zahl bleiben
    /// außen vor — die trägt man von Hand ein.
    /// </summary>
    public static IReadOnlyList<TagMatch> Match(
        IReadOnlyList<AreaRow> areas,
        IReadOnlyList<HostTagValue> tagValues)
    {
        // Schluessel ist Nummernkreis + Zahl, nie die Zahl allein — siehe der
        // filiale_04-Fall in der Klassenbeschreibung.
        var index = new Dictionary<string, List<AreaRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in areas)
        {
            if (PrefixFor(area) is not { } prefix) continue;
            foreach (var number in KeysFor(area.ExternalCode))
                (index.TryGetValue($"{prefix}_{number}", out var list)
                    ? list : index[$"{prefix}_{number}"] = []).Add(area);
        }

        var result = new List<TagMatch>();

        foreach (var tag in tagValues.OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (PrefixOfTag(tag.Value) is not { } tagPrefix) continue;
            if (NumberOfTag(tag.Value) is not { } number) continue;
            if (!index.TryGetValue($"{tagPrefix}_{number}", out var hits) || hits.Count == 0) continue;

            var chosen = hits[0];
            result.Add(new TagMatch(
                tag.Value,
                tag.HostCount,
                chosen.AreaId,
                chosen.Name,
                chosen.HostTag,
                [.. hits.Skip(1).Select(a => a.Name)]));
        }

        return result;
    }
}
