using System.Text;
using System.Text.Json;
using NLog;

namespace Checkmk.App.Services;

public sealed class SshCredentialEntry
{
    public string Host { get; set; } = "";
    public string User { get; set; } = "";
    /// <summary>DPAPI-verschluesseltes Passwort (Base64) oder null.</summary>
    public string? ProtectedPassword { get; set; }
}

public sealed class SshCredentialState
{
    public List<SshCredentialEntry> Credentials { get; set; } = new();
}

/// <summary>SSH-Login-Namen (+ optional Passwoerter) — bewusst nur user-lokal.
/// Passwoerter werden mit DPAPI-CurrentUser verschluesselt gespeichert (der
/// bestehende <see cref="ISecretProtector"/>-User-Scope-Weg).</summary>
public interface ISshCredentialStore
{
    SshCredentialEntry? Get(string host);
    void Save(string host, string user, string? plainPassword);
    void Remove(string host);
    string FilePath { get; }
}

public sealed class SshCredentialStore : ISshCredentialStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly ISecretProtector _protector;
    private readonly string _path;

    public string FilePath => _path;

    public SshCredentialStore(ISecretProtector protector)
    {
        _protector = protector;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "ssh-creds.json");
    }

    public SshCredentialEntry? Get(string host)
        => Load().Credentials
            .FirstOrDefault(c => string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entschluesseltes Klartext-Passwort oder null.</summary>
    public string? DecryptPassword(SshCredentialEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ProtectedPassword)) return null;
        try
        {
            var blob = Convert.FromBase64String(entry.ProtectedPassword);
            return Encoding.UTF8.GetString(_protector.Unprotect(blob));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "SSH-Password konnte nicht entschluesselt werden (Host={Host}).", entry.Host);
            return null;
        }
    }

    public void Save(string host, string user, string? plainPassword)
    {
        var state = Load();
        var existing = state.Credentials
            .FirstOrDefault(c => string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new SshCredentialEntry { Host = host };
            state.Credentials.Add(existing);
        }
        existing.User = user;
        existing.ProtectedPassword = string.IsNullOrEmpty(plainPassword)
            ? null
            : Convert.ToBase64String(_protector.Protect(Encoding.UTF8.GetBytes(plainPassword)));
        Persist(state);
    }

    public void Remove(string host)
    {
        var state = Load();
        var removed = state.Credentials.RemoveAll(c
            => string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Persist(state);
    }

    private SshCredentialState Load()
    {
        if (!File.Exists(_path)) return new SshCredentialState();
        try
        {
            return JsonSerializer.Deserialize<SshCredentialState>(File.ReadAllText(_path))
                   ?? new SshCredentialState();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ssh-creds.json konnte nicht gelesen werden.");
            return new SshCredentialState();
        }
    }

    private void Persist(SshCredentialState state)
    {
        try
        {
            File.WriteAllText(_path,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ssh-creds.json konnte nicht gespeichert werden.");
        }
    }
}
