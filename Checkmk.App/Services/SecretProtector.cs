using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Checkmk.App.Services;

public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] blob);
}

/// <summary>Waehlt die passende Implementierung fuer den jeweiligen Use-Case.</summary>
public static class SecretProtectorFactory
{
    /// <summary>User-Scope: DPAPI-CurrentUser. Fuer alles, was pro Nutzer bleibt
    /// (Filter, Skip-Version, Bootstrap).</summary>
    public static ISecretProtector Create() => new WindowsDpapiProtector();

    /// <summary>Shared-Scope fuer die zentrale Verbindungsdatei auf einem Fileshare,
    /// den mehrere Windows-Clients lesen. AES-GCM mit einem im Binary abgeleiteten
    /// Schluessel — wer die App hat, kann entschluesseln.</summary>
    public static ISecretProtector CreateForSharedConnection() => new SharedAesProtector();
}

/// <summary>DPAPI (CurrentUser). Nutzt Windows-eigene Verschluesselung.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kroste.Checkmk.v1");

    public byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] blob)
        => ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// AES-GCM mit einem Schluessel, der ueber PBKDF2 aus einer im Binary hinterlegten
/// Passphrase abgeleitet wird. Fuer Verbindungsdateien, die von mehreren Windows-
/// Clients auf einem Fileshare gelesen werden — jeder Client kommt mit demselben
/// Binary, kann also entschluesseln.
/// Sicherheitsniveau: schuetzt vor Zufallseinsicht auf dem Share (Klartext-
/// Vermeidung), nicht vor einem Angreifer mit App-Binary. Fuer echte Multi-User-
/// Isolation ohne Shared-Key-Problem: DPAPI-NG mit AD-Gruppen-SID (Roadmap).
/// </summary>
/// <remarks>Format: [12 Byte Nonce][16 Byte Tag][CipherText].</remarks>
public sealed class SharedAesProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 100_000;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Kroste.Checkmk.SharedV1.Salt");
    private static readonly byte[] Passphrase = Encoding.UTF8.GetBytes("Kroste.Checkmk.SharedV1");
    private static readonly byte[] Key =
        Rfc2898DeriveBytes.Pbkdf2(Passphrase, Salt, Iterations, HashAlgorithmName.SHA256, 32);

    public byte[] Protect(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);

        var result = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
        return result;
    }

    public byte[] Unprotect(byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Ungueltiges Secret-Blob-Format.");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(Key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
