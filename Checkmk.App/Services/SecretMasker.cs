using System.Text.RegularExpressions;

namespace Checkmk.App.Services;

/// <summary>
/// Ersetzt Secrets (Passwoerter, Tokens, Automation-Secrets) in Log-Text durch ***.
/// Wird vom NLog-<c>masked</c>-Layout-Renderer genutzt, damit Secrets NIE im Log
/// landen — auch nicht auf Trace-Level und auch nicht, wenn ein neuer Log-Aufruf
/// ein Passwort ungeschuetzt mitgibt.
/// </summary>
public static partial class SecretMasker
{
    private const string Mask = "***";

    // -P <wert>  (cmk-agent-ctl register -P <passwort>)
    [GeneratedRegex(@"(-P\s+)(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex PFlag();

    // key=wert / key: wert  fuer secret/password/passwort/pwd/token/apikey
    [GeneratedRegex(
        @"\b(secret|password|passwort|pwd|token|api[_-]?key)\b(\s*[:=]\s*)(\S+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeyValue();

    // Bearer <token> / Authorization: <schema> <token>
    [GeneratedRegex(@"(Bearer\s+)(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        text = PFlag().Replace(text, "$1" + Mask);
        text = KeyValue().Replace(text, "$1$2" + Mask);
        text = Bearer().Replace(text, "$1" + Mask);
        return text;
    }
}
