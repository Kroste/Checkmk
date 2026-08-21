using System.Text.RegularExpressions;

namespace Checkmk.App.Services;

/// <summary>
/// Ordnet Hosts über ein Namensmuster einem Bereich zu.
///
/// Grundlage sind die tatsächlichen Namenskonventionen: Die Schulnummer steckt
/// im Hostnamen, aber an wechselnder Stelle — <c>46-SW04</c>, <c>46-USV</c>,
/// <c>NAS46-01</c>, <c>PA46-01</c>, <c>ESX46-02</c>, <c>iRMC-46</c>,
/// <c>WLC-46…</c>. Ein simples „enthält 46" reicht deshalb nicht: Es träfe
/// auch <c>146-SW01</c> und <c>46x</c>.
/// </summary>
public static class HostPatternMatcher
{
    // Harter Cap gegen catastrophic backtracking bei von Hand getippten
    // Mustern — dieselbe Vorsichtsmassnahme wie im Host-Filter.
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Baut aus einer Standortnummer das Muster. Gesetzt wird eine
    /// <b>Ziffern-Grenze</b>: <c>46</c> trifft <c>46-SW04</c> und
    /// <c>NAS46-01</c>, aber <b>nicht</b> <c>146-SW01</c> oder <c>460</c>.
    /// Ohne diese Grenze bekäme Schule 4 alle Hosts der Schulen 40–49.
    ///
    /// Erwartet <b>genau eine</b> Nummer. Alles andere — Kürzel wie
    /// <c>SFT</c> oder <c>OSZ III</c>, aber auch Doppelangaben wie
    /// <c>25/26</c> — ergibt <c>null</c>. Welche Nummer bei zusammengelegten
    /// Schulen tatsächlich benutzt wird, steht nirgends in den Daten und lässt
    /// sich nicht erraten; das klärt der Aufrufer
    /// (<see cref="PotsdamPlaceImporter.EffectiveCode"/>).
    /// </summary>
    public static string? FromCode(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!trimmed.All(char.IsAsciiDigit)) return null;

        var number = trimmed.TrimStart('0');
        if (number.Length == 0) number = "0";

        return $@"(?<!\d){number}(?!\d)";
    }

    /// <summary>
    /// Passt der Hostname auf das Muster? Ungültige Muster treffen
    /// <b>nichts</b> — so merkt man den Tippfehler daran, dass keine
    /// Vorschläge kommen, statt an einer Ausnahme mitten im Ablauf.
    /// </summary>
    public static bool Matches(string? pattern, string hostName)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(hostName))
            return false;

        try
        {
            return Regex.IsMatch(hostName, pattern, RegexOptions.IgnoreCase, Timeout);
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Ist das Muster überhaupt übersetzbar? Für die Rückmeldung im Dialog.</summary>
    public static bool IsValid(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        try
        {
            _ = Regex.Match("", pattern, RegexOptions.IgnoreCase, Timeout);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
