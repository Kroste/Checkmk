using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Checkmk.App.Services;

public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] blob);
}

/// <summary>Waehlt die passende Implementierung fuer die aktuelle Plattform.</summary>
public static class SecretProtectorFactory
{
    public static ISecretProtector Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsDpapiProtector();
        if (OperatingSystem.IsLinux())
            return new LinuxMachineKeyProtector();
        return new NoopProtector();
    }
}

/// <summary>DPAPI (CurrentUser). Windows-spezifisch, wie vorher.</summary>
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
/// AES-GCM mit einem Schluessel, der aus /etc/machine-id + User-UID + statischer
/// Entropy abgeleitet wird. Bindet das Secret an Rechner+User (vergleichbar zu
/// DPAPI-CurrentUser). Kein Ersatz fuer SecretService/libsecret, aber deutlich
/// besser als Klartext und ohne Desktop-Session/Keyring-Daemon nutzbar.
/// </summary>
/// <remarks>Format: [12 Byte Nonce][16 Byte Tag][CipherText].</remarks>
public sealed class LinuxMachineKeyProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kroste.Checkmk.v1");

    private readonly byte[] _key = DeriveKey();

    public byte[] Protect(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
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

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static byte[] DeriveKey()
    {
        // machine-id ist stabil pro Installation, root-lesbar; /var/lib/dbus/machine-id
        // ist ein Symlink auf /etc/machine-id auf modernen Systemen.
        var machineId = ReadFirstExisting("/etc/machine-id", "/var/lib/dbus/machine-id")
                        ?? "no-machine-id";
        var uid = Environment.UserName + ":" + Environment.GetEnvironmentVariable("UID");

        var material = new byte[Entropy.Length
                                + Encoding.UTF8.GetByteCount(machineId)
                                + Encoding.UTF8.GetByteCount(uid)];
        var offset = 0;
        Buffer.BlockCopy(Entropy, 0, material, offset, Entropy.Length);
        offset += Entropy.Length;
        offset += Encoding.UTF8.GetBytes(machineId, 0, machineId.Length, material, offset);
        Encoding.UTF8.GetBytes(uid, 0, uid.Length, material, offset);

        return SHA256.HashData(material);
    }

    private static string? ReadFirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "machine-id konnte nicht gelesen werden: {Path}", path);
            }
        }
        return null;
    }
}

/// <summary>Fallback fuer nicht unterstuetzte Plattformen (macOS derzeit) — Klartext mit Warnung.</summary>
public sealed class NoopProtector : ISecretProtector
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public byte[] Protect(byte[] data)
    {
        Log.Warn("Keine plattformspezifische Secret-Verschluesselung — Secret wird UNVERSCHLUESSELT abgelegt.");
        return data;
    }

    public byte[] Unprotect(byte[] blob) => blob;
}
