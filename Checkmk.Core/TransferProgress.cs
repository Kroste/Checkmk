namespace Checkmk.Core;

/// <summary>
/// Fortschritt eines laufenden REST-Abrufs. <see cref="TotalBytes"/> ist
/// <c>null</c>, wenn der Server kein <c>Content-Length</c> liefert — bei den
/// grossen Livestatus-Antworten ist das der Normalfall (chunked transfer).
/// Der Aufrufer muss die Gesamtgroesse dann schaetzen (z. B. aus dem letzten Lauf).
/// </summary>
public readonly record struct TransferProgress(long BytesRead, long? TotalBytes)
{
    /// <summary>Anteil 0..1, oder <c>null</c> wenn die Gesamtgroesse unbekannt ist.</summary>
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesRead / TotalBytes.Value, 0, 1)
        : null;
}
