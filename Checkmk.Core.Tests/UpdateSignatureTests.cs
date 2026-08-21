using System.Security.Cryptography;
using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Signaturprüfung für Updates.
///
/// Die Lücke, die das schließt: Die Adresse des Update-Kanals steht in
/// <c>GlobalSetting.UpdateChannelUrl</c>, und auf diese Tabelle darf das
/// Laufzeitkonto schreiben. Ohne Signatur genügt ein <c>UPDATE</c> in der
/// Datenbank, um 48 Rechnern ein beliebiges ZIP unterzuschieben, das sie
/// entpacken und starten.
/// </summary>
public class UpdateSignatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cockpit-sig-tests-" + Guid.NewGuid().ToString("N"));

    private readonly string _privateKey;
    private readonly string _publicKey;

    public UpdateSignatureTests()
    {
        Directory.CreateDirectory(_dir);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
        _publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Best-effort */ }
        GC.SuppressFinalize(this);
    }

    private string WritePackage(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private SignedUpdateManifest SignFor(string zipPath, string version, string? key = null)
    {
        var manifest = new UpdateManifest(
            version, Path.GetFileName(zipPath),
            UpdateSignature.HashFile(zipPath), new FileInfo(zipPath).Length);

        return new SignedUpdateManifest
        {
            Version = manifest.Version,
            File = manifest.File,
            Sha256 = manifest.Sha256,
            Size = manifest.Size,
            Signature = UpdateSignature.Sign(manifest, key ?? _privateKey)
        };
    }

    // --- Der gute Fall ---------------------------------------------------

    [Fact]
    public void A_correctly_signed_package_passes()
    {
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "das ist das echte Paket");

        var result = UpdateSignature.Verify(SignFor(zip, "1.14.0"), zip, "1.14.0", _publicKey);

        result.Ok.Should().BeTrue();
        result.Reason.Should().Contain("gueltig");
    }

    [Fact]
    public void The_manifest_survives_the_round_trip_through_json()
    {
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "Paket");
        var json = UpdateSignature.ToJson(SignFor(zip, "1.14.0"));

        var back = UpdateSignature.FromJson(json);

        UpdateSignature.Verify(back, zip, "1.14.0", _publicKey).Ok.Should().BeTrue();
    }

    // --- Angriffsfaelle --------------------------------------------------

    [Fact]
    public void A_tampered_package_is_rejected()
    {
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "das ist das echte Paket");
        var signed = SignFor(zip, "1.14.0");

        // Der Angreifer tauscht das ZIP, laesst das Manifest aber stehen.
        File.WriteAllText(zip, "das ist das echte Paketx");

        var result = UpdateSignature.Verify(signed, zip, "1.14.0", _publicKey);

        result.Ok.Should().BeFalse();
        result.Reason.Should().ContainAny("Groesse", "Pruefsumme");
    }

    [Fact]
    public void A_package_signed_with_another_key_is_rejected()
    {
        // Der Kern der Sache: Wer die Kanal-Adresse umbiegt, hat trotzdem nicht
        // den Schluessel des Herausgebers.
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var strangerKey = Convert.ToBase64String(stranger.ExportPkcs8PrivateKey());

        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "untergeschobenes Paket");
        var signed = SignFor(zip, "1.14.0", strangerKey);

        var result = UpdateSignature.Verify(signed, zip, "1.14.0", _publicKey);

        result.Ok.Should().BeFalse();
        result.Reason.Should().Contain("Herausgeber");
    }

    [Fact]
    public void A_valid_manifest_for_an_older_package_is_rejected()
    {
        // Downgrade: ein echt signiertes ALTES Paket als neues ausgeben, um eine
        // inzwischen geschlossene Luecke zurueckzuholen.
        var zip = WritePackage("Checkmk-1.2.0-win-x64.zip", "alte Version");
        var signed = SignFor(zip, "1.2.0");

        var result = UpdateSignature.Verify(signed, zip, "1.14.0", _publicKey);

        result.Ok.Should().BeFalse();
        result.Reason.Should().Contain("1.2.0");
    }

    [Fact]
    public void A_manifest_for_a_different_file_is_rejected()
    {
        var real = WritePackage("Checkmk-1.14.0-win-x64.zip", "Inhalt");
        var signed = SignFor(real, "1.14.0");

        // Gleicher Inhalt, anderer Name — das Manifest bindet an den Dateinamen.
        var other = WritePackage("Checkmk-1.14.0-win-x86.zip", "Inhalt");

        UpdateSignature.Verify(signed, other, "1.14.0", _publicKey).Ok.Should().BeFalse();
    }

    [Fact]
    public void A_missing_manifest_is_rejected_when_a_key_is_configured()
    {
        // „Kein Manifest" darf nie „dann eben ungeprueft" heissen — wer den
        // Download umlenken kann, kann auch das Manifest verschwinden lassen.
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "Paket");

        var result = UpdateSignature.Verify(null, zip, "1.14.0", _publicKey);

        result.Ok.Should().BeFalse();
        result.Reason.Should().Contain("Pflicht");
    }

    [Theory]
    [InlineData("nicht mal base64!")]
    [InlineData("")]
    public void A_broken_signature_counts_as_not_verified(string signature)
    {
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "Paket");
        var signed = SignFor(zip, "1.14.0");
        signed.Signature = signature;

        UpdateSignature.Verify(signed, zip, "1.14.0", _publicKey).Ok.Should().BeFalse();
    }

    // --- Schalter --------------------------------------------------------

    [Fact]
    public void Without_a_configured_key_nothing_is_checked()
    {
        // So bleiben bestehende Releases installierbar, bis ein Schluesselpaar
        // erzeugt und ins Binary eingetragen ist.
        var zip = WritePackage("Checkmk-1.14.0-win-x64.zip", "Paket");

        UpdateSignature.Verify(null, zip, "1.14.0", "").Ok.Should().BeTrue();
        UpdateSignature.IsEnforced.Should().Be(!string.IsNullOrWhiteSpace(
            UpdateSignature.PublicKeyBase64));
    }

    // --- Versionsvergleich ----------------------------------------------

    [Theory]
    [InlineData("1.14.0", "1.14.0")]
    [InlineData("v1.14.0", "1.14.0")]
    [InlineData("1.14.0", "1.14.0.0")]
    [InlineData("1.14.0+abc123", "1.14.0")]
    public void Common_spellings_of_the_same_version_match(string a, string b)
        => UpdateSignature.VersionMatches(a, b).Should().BeTrue();

    [Theory]
    [InlineData("1.14.0", "1.14.1")]
    [InlineData("1.14.0", "2.0.0")]
    [InlineData("kaputt", "1.14.0")]
    public void Different_or_unparsable_versions_do_not_match(string a, string b)
        => UpdateSignature.VersionMatches(a, b).Should().BeFalse();
}
