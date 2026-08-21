namespace Checkmk.Data;

/// <summary>
/// Einzeiler-Tabelle mit der Schema-Version. Die Anwendung migriert bewusst
/// nicht selbst (siehe db/README.md) — sie vergleicht nur und meldet, wenn
/// Schema und Programmstand auseinanderlaufen.
/// </summary>
public sealed class SchemaVersionRow
{
    public int Id { get; set; } = 1;
    public int Version { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public string AppliedBy { get; set; } = "";
}

/// <summary>
/// Globale Einstellung als Schluessel/Wert. Bewusst nicht typisiert je Spalte:
/// eine neue Einstellung soll keinen DDL-Termin mit dem SA-Konto brauchen.
/// </summary>
public sealed class GlobalSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string ChangedBy { get; set; } = "";
}

/// <summary>
/// Host → Domain. Loest die geteilte <c>hosts.json</c> ab, bei der jeder
/// Speichervorgang die komplette Datei zurueckschrieb und gleichzeitige
/// Bearbeiter sich gegenseitig ueberschrieben.
/// </summary>
public sealed class HostDomain
{
    public string HostName { get; set; } = "";
    public string Domain { get; set; } = "";
    public DateTime ChangedAtUtc { get; set; }
    public string ChangedBy { get; set; } = "";
}

/// <summary>
/// Ein Ort — geteilt und hierarchisch. Stadtsicht und Campus-Sicht sind
/// dasselbe auf zwei Zoomstufen, deshalb ein Baum statt zweier Begriffe.
///
/// <see cref="GeometryJson"/> haelt spaeter das Polygon fuer die Karte
/// (GeoJSON, WGS84) und bleibt bis dahin leer — der Bereichsbaum ist auch
/// ohne Karte nutzbar, und genau deshalb kommt er zuerst.
/// </summary>
public sealed class Area
{
    public int AreaId { get; set; }
    public int? ParentAreaId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Flaeche (GeoJSON-Polygon, WGS84). Optional — die meisten
    /// Standorte sind ein Punkt.</summary>
    public string? GeometryJson { get; set; }

    /// <summary>Punktlage. Ein Bereich mit beidem wird als Flaeche gezeichnet;
    /// der Punkt bleibt als Sprungziel erhalten.</summary>
    public double? Lat { get; set; }
    public double? Lon { get; set; }

    /// <summary>Anschrift zur Anzeige. Ohne sie ist ein Marker auf der Karte
    /// schwer einer Aussenstelle zuzuordnen.</summary>
    public string? Address { get; set; }

    /// <summary>Herkunft eines importierten Standorts (z. B.
    /// <c>LHP-Verwaltungsstandorte</c>) und dessen Kennung dort. Beide leer bei
    /// von Hand angelegten Bereichen.</summary>
    public string? ExternalSource { get; set; }
    public string? ExternalId { get; set; }

    public string? MapLayerKey { get; set; }
    public int SortOrder { get; set; }
    public int? OwningTeamId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string ChangedBy { get; set; } = "";
}

/// <summary>
/// Wo ein Geraet steht. <see cref="HostName"/> ist Primaerschluessel: genau ein
/// Bereich pro Host, weil ein Geraet an genau einem Ort steht. Damit ist die
/// Zuordnung geteilt statt pro Team gepflegt — wer einen Switch umtraegt,
/// aendert eine Zeile und alle Sichten stimmen wieder.
/// </summary>
public sealed class HostArea
{
    public string HostName { get; set; } = "";
    public int AreaId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public string AssignedBy { get; set; } = "";
}
