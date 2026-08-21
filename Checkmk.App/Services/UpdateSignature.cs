using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Signiertes Manifest zu einem Release. Liegt als <c>update.json</c> neben dem
/// ZIP im Release.
/// </summary>
/// <param name="Version">Muss zum Tag des Releases passen.</param>
/// <param name="File">Dateiname des ZIPs — bindet die Signatur an genau dieses Paket.</param>
/// <param name="Sha256">Hex-Prüfsumme des ZIPs, Kleinbuchstaben.</param>
/// <param name="Size">Größe in Bytes. Redundant zur Prüfsumme, aber sie schlägt
/// bei einem abgeschnittenen Download früher und mit klarerer Meldung an.</param>
/// <remarks>Wird nie serialisiert — auf die Platte geht
/// <see cref="SignedUpdateManifest"/>. Dieser Typ hält nur die Felder zusammen,
/// über die signiert wird.</remarks>
public sealed record UpdateManifest(
    string Version,
    string File,
    string Sha256,
    long Size)
{
    /// <summary>
    /// Genau die Bytes, über die signiert wird. Bewusst ein eigener, fest
    /// definierter Text und <b>nicht</b> das JSON der Datei: Zwei
    /// JSON-Serialisierer schreiben Leerzeichen und Reihenfolge verschieden,
    /// und schon eine Zeilenendenormalisierung beim Kopieren würde jede
    /// Signatur ungültig machen.
    /// </summary>
    public byte[] SignedBytes()
        => Encoding.UTF8.GetBytes($"checkmk-cockpit/1\n{Version}\n{File}\n{Sha256.ToLowerInvariant()}\n{Size}");
}

/// <summary>Manifest samt Signatur, so wie die Datei aussieht.</summary>
public sealed class SignedUpdateManifest
{
    public string Version { get; set; } = "";
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }

    /// <summary>ECDSA-P-256-Signatur über <see cref="UpdateManifest.SignedBytes"/>,
    /// Base64.</summary>
    public string Signature { get; set; } = "";

    public UpdateManifest ToManifest() => new(Version, File, Sha256, Size);
}

/// <summary>Ergebnis einer Prüfung — mit Grund, damit die Meldung etwas taugt.</summary>
public sealed record SignatureResult(bool Ok, string Reason)
{
    public static SignatureResult Pass(string reason) => new(true, reason);
    public static SignatureResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Prüft ein heruntergeladenes Update-ZIP gegen ein signiertes Manifest.
///
/// <para><b>Warum überhaupt?</b> Die Adresse des Update-Kanals steht in
/// <c>GlobalSetting.UpdateChannelUrl</c>, und auf diese Tabelle darf das
/// Laufzeitkonto schreiben. Ohne Signatur genügt also ein <c>UPDATE</c> in der
/// Datenbank, um 48 Rechnern ein beliebiges ZIP unterzuschieben, das sie
/// entpacken und starten. Die Signatur schließt genau diese Lücke — und zwar
/// unabhängig davon, ob der Kanal auf GitHub oder auf einem Fileshare liegt.</para>
///
/// <para><b>Der öffentliche Schlüssel steckt im Binary</b>
/// (<see cref="PublicKeyBase64"/>), nicht in der Datenbank. Läge er dort, könnte
/// derselbe Zugriff, der die Adresse ändert, auch den Schlüssel austauschen —
/// die Prüfung wäre wertlos.</para>
///
/// <para><b>ECDSA P-256 statt des in der Roadmap notierten Ed25519.</b> .NET 10
/// bringt kein Ed25519 mit (nachgemessen: <c>System.Security.Cryptography</c>
/// kennt ML-DSA und SLH-DSA, aber kein Ed25519). Es nachzurüsten hieße
/// BouncyCastle, und ein zusätzliches NuGet-Paket ist in diesem Netz teuer —
/// jedes muss von Hand ins Offline-Bundle geholt werden. P-256 mit SHA-256 ist
/// eingebaut und für diesen Zweck genauso tragfähig.</para>
/// </summary>
public static class UpdateSignature
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Öffentlicher Schlüssel (SubjectPublicKeyInfo, Base64) der Stelle, die
    /// Releases signiert. <b>Leer heißt: keine Prüfung.</b>
    ///
    /// <para><b>Der Wert ist absichtlich leer und soll es bleiben.</b> Das ist
    /// keine offene Aufgabe. Alle Nutzer dieses Cockpits sind
    /// Systemadministratoren zentraler Dienste, und Schreibrecht auf dem
    /// Update-Ordner haben zwei Personen — der Vertrauensanker ist die
    /// NTFS-Berechtigung, nicht ein Schlüssel. Ausführlich in CLAUDE.md §4.</para>
    ///
    /// <para>Einschalten lohnt erst, wenn das Cockpit über diesen Kreis hinaus
    /// verteilt wird oder der Kanal in ein weniger kontrolliertes Netz wandert.
    /// Dann: <c>Checkmk.App.exe --make-update-key</c>, den öffentlichen Teil
    /// hier eintragen — ab da ist ein gültiges Manifest <b>Pflicht</b>, und ein
    /// Update ohne Signatur wird abgelehnt statt durchgewunken.</para>
    /// </summary>
    public const string PublicKeyBase64 = "";

    /// <summary>Ist die Prüfung überhaupt eingeschaltet?</summary>
    public static bool IsEnforced => !string.IsNullOrWhiteSpace(PublicKeyBase64);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ToJson(SignedUpdateManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOpts);

    public static SignedUpdateManifest? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<SignedUpdateManifest>(json, JsonOpts); }
        catch (JsonException ex)
        {
            Log.Warn(ex, "Update-Manifest ist kein gueltiges JSON.");
            return null;
        }
    }

    /// <summary>Hex-SHA-256 einer Datei, Kleinbuchstaben.</summary>
    public static string HashFile(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Signiert ein Manifest mit einem privaten Schlüssel (PKCS#8, Base64).</summary>
    public static string Sign(UpdateManifest manifest, string privateKeyBase64)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        return Convert.ToBase64String(
            key.SignData(manifest.SignedBytes(), HashAlgorithmName.SHA256));
    }

    /// <summary>
    /// Prüft Manifest und Datei.
    ///
    /// Die Reihenfolge ist Absicht: erst die Signatur (billig, und sie
    /// entscheidet über die Vertrauenswürdigkeit aller anderen Angaben), dann
    /// Größe, dann die Prüfsumme über die ganze Datei.
    /// </summary>
    public static SignatureResult Verify(SignedUpdateManifest? signed, string zipPath,
        string expectedVersion, string? publicKeyBase64 = null)
    {
        var key = publicKeyBase64 ?? PublicKeyBase64;

        if (string.IsNullOrWhiteSpace(key))
            return SignatureResult.Pass("Signaturpruefung ist nicht eingerichtet.");

        if (signed is null)
            return SignatureResult.Fail(
                "Zum Release gibt es kein update.json. Ein signiertes Manifest ist Pflicht, "
              + "solange ein Schluessel hinterlegt ist.");

        var manifest = signed.ToManifest();

        if (!TryVerifySignature(manifest, signed.Signature, key))
            return SignatureResult.Fail(
                "Die Signatur des Manifests stimmt nicht. Das Paket stammt nicht von der "
              + "Stelle, die dieses Cockpit als Herausgeber kennt.");

        // Die Version muss zum Release passen, sonst liesse sich ein gueltig
        // signiertes ALTES Paket als neues ausgeben (Downgrade-Angriff).
        if (!VersionMatches(manifest.Version, expectedVersion))
            return SignatureResult.Fail(
                $"Das Manifest ist fuer Version {manifest.Version}, angeboten wurde "
              + $"{expectedVersion}.");

        var actualName = Path.GetFileName(zipPath);
        if (!string.Equals(manifest.File, actualName, StringComparison.OrdinalIgnoreCase))
            return SignatureResult.Fail(
                $"Das Manifest gilt fuer '{manifest.File}', geladen wurde '{actualName}'.");

        var size = new FileInfo(zipPath).Length;
        if (size != manifest.Size)
            return SignatureResult.Fail(
                $"Groesse weicht ab: erwartet {manifest.Size} Bytes, geladen {size}. "
              + "Vermutlich ein abgebrochener Download.");

        var hash = HashFile(zipPath);
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            return SignatureResult.Fail("Die Pruefsumme des Pakets stimmt nicht mit dem Manifest ueberein.");

        return SignatureResult.Pass($"Signatur gueltig (SHA-256 {hash[..12]}…).");
    }

    private static bool TryVerifySignature(UpdateManifest manifest, string signatureBase64,
        string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(manifest.SignedBytes(),
                Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Kaputte Base64 oder ein Schluessel der falschen Kurve zaehlen als
            // „nicht verifiziert" — nie als „konnte nicht pruefen, also durch".
            Log.Warn(ex, "Signatur konnte nicht geprueft werden.");
            return false;
        }
    }

    /// <summary>
    /// Versionsvergleich, der <c>v1.14.0</c>, <c>1.14.0</c> und <c>1.14.0.0</c>
    /// als dasselbe ansieht. Strikte Stringgleichheit wäre eine Stolperfalle
    /// beim Erzeugen des Manifests, ohne irgendetwas sicherer zu machen.
    /// </summary>
    internal static bool VersionMatches(string a, string b)
    {
        static Version? Norm(string s)
        {
            var t = s.Trim().TrimStart('v', 'V');
            var plus = t.IndexOf('+');
            if (plus >= 0) t = t[..plus];
            return Version.TryParse(t, out var v)
                ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0))
                : null;
        }

        var va = Norm(a);
        var vb = Norm(b);
        return va is not null && vb is not null && va == vb;
    }
}
