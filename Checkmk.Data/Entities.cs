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

    /// <summary>Regulaerer Ausdruck auf Hostnamen fuer Zuordnungsvorschlaege.</summary>
    public string? HostPattern { get; set; }

    /// <summary>Code aus der Herkunftsquelle (z. B. SCHULNUM). Getrennt vom
    /// Muster, damit ein erneuter Import ein von Hand angepasstes Muster nicht
    /// ueberschreibt.</summary>
    public string? ExternalCode { get; set; }

    /// <summary>
    /// Wert eines Checkmk-Ortstags (z. B. <c>schule_46</c>), der die Hosts
    /// dieses Bereichs traegt. Staerkeres Signal als
    /// <see cref="HostPattern"/> und gewinnt deshalb: Der Tag ist im
    /// Checkmk-Setup gepflegt, das Muster nur aus dem Namen erschlossen.
    ///
    /// Gespeichert wird der <b>Wert</b>, nicht der Schluessel — welcher
    /// Attribut-Schluessel gilt, ist eine Eigenschaft der Umgebung und steht
    /// in <c>GlobalSetting.HostLocationTagKeys</c>.
    /// </summary>
    public string? HostTag { get; set; }

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
/// <summary>
/// In welchen Checkmk-Sites ein Bereich sichtbar ist. Reiner
/// Sichtbarkeitsfilter, kein Eigentum: Ein Ort ist ein Ort, und im Stadthaus
/// kann Technik aus beiden Sites stehen.
///
/// <b>Keine Zeile für einen Bereich = in allen Sites sichtbar.</b> Deshalb
/// bleiben bestehende Bereiche unverändert, und die Zusammenführung der Sites
/// ist ein DELETE auf diese Tabelle.
/// </summary>
public sealed class AreaSite
{
    public int AreaId { get; set; }
    public string Site { get; set; } = "";
    public DateTime AddedAtUtc { get; set; }
}

public sealed class HostArea
{
    public string HostName { get; set; } = "";
    public int AreaId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public string AssignedBy { get; set; } = "";
}

/// <summary>
/// Ein Team — reine <b>Organisation, kein Zugriffsschutz</b>. Alle 48 Personen
/// duerfen alle Hosts sehen, und das Laufzeitkonto der Anwendung kann diese
/// Tabellen ohnehin schreiben. Teams buendeln geteilte Filter und Sichten.
/// </summary>
public sealed class Team
{
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Mitgliedschaft, n:m — wer AD und Exchange macht, steht in beiden Teams.
/// <see cref="UserName"/> ist der blanke Windows-Anmeldename ohne
/// Domaenenpraefix.
/// </summary>
public sealed class TeamMember
{
    public int TeamId { get; set; }
    public string UserName { get; set; } = "";
}

/// <summary>
/// Wer Teams anlegen und Anmeldungen zuordnen darf.
///
/// <b>Ist die Tabelle leer, gilt jeder als Admin</b> — eine leere Tabelle heisst
/// „noch nicht eingerichtet", und die Alternative waere eine Funktion, die ohne
/// einen SQL-Eingriff niemand benutzen kann. Sobald der erste Eintrag steht,
/// greift die Liste. Das ist vertretbar, weil Teams ausdruecklich kein
/// Zugriffsschutz sind.
/// </summary>
public sealed class AppAdmin
{
    public string UserName { get; set; } = "";
    public DateTime AddedAtUtc { get; set; }
    public string AddedBy { get; set; } = "";
}

/// <summary>
/// Ein Host-Filter. <see cref="TeamId"/> gesetzt = Team-Filter,
/// <see cref="OwnerUserName"/> gesetzt = persoenlich; genau eins von beidem
/// (CHECK in der Datenbank).
///
/// Der Alltagsgewinn steckt in der ersten Variante: Heute baut sich jeder der
/// 48 seinen eigenen, und die Urlaubsvertretung faengt bei null an.
/// </summary>
public sealed class HostFilterRow
{
    public int HostFilterId { get; set; }
    public int? TeamId { get; set; }
    public string? OwnerUserName { get; set; }
    public string Site { get; set; } = "";
    public string Name { get; set; } = "";
    public string? HostNameRegex { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string ChangedBy { get; set; } = "";
}

/// <summary>Include-Liste eines Filters. Leer = es gilt der Regex.</summary>
public sealed class HostFilterHostRow
{
    public int HostFilterId { get; set; }
    public string HostName { get; set; } = "";
}
