using System.Security.Cryptography;
using System.Text;

namespace Checkmk.Data;

/// <summary>
/// Macht den Verbindungsstring in <c>database.json</c> unlesbar für den
/// Schulterblick — <b>nicht mehr und nicht weniger</b>.
///
/// Die Methoden heißen <see cref="Obfuscate"/> und <see cref="Deobfuscate"/>
/// und nicht Encrypt/Decrypt, weil das hier keine Verschlüsselung im Sinne
/// eines Zugriffsschutzes ist: Der Schlüssel steckt im Binary, das neben der
/// Datei liegt. Wer beides hat — und beides liegt auf ~50 Arbeitsplätzen —,
/// kommt an den Klartext. Das ist derselbe ehrliche Umgang wie bei
/// <c>secretBase64</c> im Viewer-Profil.
///
/// Was tatsächlich schützt, ist das Datenbankrecht: Das Laufzeitkonto
/// <c>CheckMK_Copilot_Worker</c> darf Zeilen lesen und schreiben, sonst nichts.
/// Kein db_owner, keine anderen Datenbanken. Deshalb ist das Zwei-Konten-Modell
/// aus db/README.md keine Förmlichkeit, sondern die einzige wirksame Grenze.
///
/// Wozu es dann überhaupt? Damit ein Passwort nicht im Klartext in einer Datei
/// steht, die in Backups, Ticketanhängen und über die Schulter landet. Das
/// verhindert Zufallsfunde, keine Angreifer.
/// </summary>
public static class ConnectionStringObfuscator
{
    /// <summary>Kennzeichnet einen verschleierten Wert. Ohne das Präfix gilt ein
    /// Wert als Klartext — so bleiben handgeschriebene Testdateien benutzbar.</summary>
    public const string Prefix = "obf1:";

    // Fester Schlüssel im Binary. Bewusst kein Geheimnis (siehe Klassenkommentar);
    // er dient nur dazu, dass der Wert nicht wie ein Passwort aussieht.
    private const string KeyMaterial = "Kroste.Checkmk.Cockpit/database.json/v1";

    private const int NonceSize = 12;   // AES-GCM Standard
    private const int TagSize = 16;

    private static byte[] Key() => SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial));

    public static bool IsObfuscated(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Obfuscate(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(Key(), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        // nonce || tag || cipher — feste Längen vorn, damit das Zerlegen trivial bleibt.
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);

        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Gibt den Klartext zurück. Werte ohne <see cref="Prefix"/> werden
    /// unverändert durchgereicht — eine von Hand geschriebene Datei mit
    /// Klartext soll funktionieren, nicht mit einer Fehlermeldung abbrechen.
    /// </summary>
    public static string Deobfuscate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsObfuscated(value)) return value;

        var blob = Convert.FromBase64String(value[Prefix.Length..]);
        if (blob.Length < NonceSize + TagSize)
            throw new FormatException("Verschleierter Wert ist zu kurz — Datei beschädigt?");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(Key(), TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
