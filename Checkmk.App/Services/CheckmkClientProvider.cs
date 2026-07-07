using System.Net;
using Checkmk.Core;

namespace Checkmk.App.Services;

/// <summary>
/// Liefert einen CheckmkClient auf Basis der aktuellen Verbindungseinstellungen.
/// Wird nach dem Speichern der Settings neu konfiguriert, ohne die App neu zu starten.
/// </summary>
public interface ICheckmkClientProvider
{
    /// <summary>Aktueller Client oder null, wenn (noch) nicht konfiguriert.</summary>
    CheckmkClient? Current { get; }

    bool IsReady { get; }

    /// <summary>Baut den Client aus Settings + Klartext-Secret neu.</summary>
    void Configure(ConnectionSettings settings, string plainSecret);
}

public sealed class CheckmkClientProvider : ICheckmkClientProvider
{
    private HttpClient? _http;

    public CheckmkClient? Current { get; private set; }

    public bool IsReady => Current is not null;

    public void Configure(ConnectionSettings settings, string plainSecret)
    {
        var options = settings.ToOptions(plainSecret);

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        if (options.IgnoreCertificateErrors)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        _http?.Dispose();
        _http = new HttpClient(handler);
        Current = new CheckmkClient(_http, options);
    }
}
