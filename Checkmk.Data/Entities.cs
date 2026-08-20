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
