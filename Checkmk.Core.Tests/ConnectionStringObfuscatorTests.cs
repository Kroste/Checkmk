using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Verschleierung, kein Zugriffsschutz — der Schluessel steckt im Binary. Die
/// Tests sichern deshalb <b>Benutzbarkeit</b> ab (Round-Trip, Klartext-Toleranz,
/// klare Fehler bei kaputten Dateien), nicht kryptografische Staerke.
/// </summary>
public class ConnectionStringObfuscatorTests
{
    private const string Sample =
        "Server=FOC-SQL01;Database=CheckMK_Copilot;User Id=CheckMK_Copilot_Worker;"
      + "Password=geheim;Encrypt=True;TrustServerCertificate=True";

    [Fact]
    public void Round_trip_returns_the_original()
    {
        var obfuscated = ConnectionStringObfuscator.Obfuscate(Sample);

        ConnectionStringObfuscator.Deobfuscate(obfuscated).Should().Be(Sample);
    }

    [Fact]
    public void Obfuscated_value_does_not_leak_the_password()
    {
        var obfuscated = ConnectionStringObfuscator.Obfuscate(Sample);

        obfuscated.Should().StartWith(ConnectionStringObfuscator.Prefix);
        obfuscated.Should().NotContain("geheim");
        obfuscated.Should().NotContain("FOC-SQL01");
    }

    [Fact]
    public void Same_input_twice_yields_different_output()
    {
        // Zufaelliger Nonce je Durchlauf: zwei Ausrollpakete mit demselben
        // String sehen nicht identisch aus.
        var a = ConnectionStringObfuscator.Obfuscate(Sample);
        var b = ConnectionStringObfuscator.Obfuscate(Sample);

        a.Should().NotBe(b);
        ConnectionStringObfuscator.Deobfuscate(a).Should().Be(Sample);
        ConnectionStringObfuscator.Deobfuscate(b).Should().Be(Sample);
    }

    [Fact]
    public void Plain_text_passes_through_unchanged()
    {
        // Eine von Hand geschriebene Testdatei mit Klartext soll funktionieren
        // und nicht mit einer Fehlermeldung abbrechen.
        ConnectionStringObfuscator.Deobfuscate(Sample).Should().Be(Sample);
        ConnectionStringObfuscator.IsObfuscated(Sample).Should().BeFalse();
    }

    [Fact]
    public void Tampered_value_is_rejected_rather_than_silently_wrong()
    {
        // AES-GCM prueft den Tag mit. Eine halb kopierte Datei liefert damit
        // einen Fehler statt eines Verbindungsstrings aus Zufallsbytes.
        var obfuscated = ConnectionStringObfuscator.Obfuscate(Sample);
        var body = obfuscated[ConnectionStringObfuscator.Prefix.Length..];
        var bytes = Convert.FromBase64String(body);
        bytes[^1] ^= 0xFF;
        var tampered = ConnectionStringObfuscator.Prefix + Convert.ToBase64String(bytes);

        var act = () => ConnectionStringObfuscator.Deobfuscate(tampered);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Truncated_value_reports_a_readable_error()
    {
        var tooShort = ConnectionStringObfuscator.Prefix + Convert.ToBase64String(new byte[4]);

        var act = () => ConnectionStringObfuscator.Deobfuscate(tooShort);

        act.Should().Throw<FormatException>().WithMessage("*beschädigt*");
    }
}
