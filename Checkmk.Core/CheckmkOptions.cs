namespace Checkmk.Core;

/// <summary>
/// Verbindungs- und Authentifizierungsparameter fuer eine Checkmk-Site.
/// </summary>
public sealed class CheckmkOptions
{
    /// <summary>Hostname oder IP des Checkmk-Servers, z. B. "monitoring.lhp.intern".</summary>
    public required string Host { get; set; }

    /// <summary>Site-Name (OMD-Site), z. B. "prod".</summary>
    public required string Site { get; set; }

    /// <summary>Automation-User (empfohlen: dedizierter Automation-User, nicht persoenlicher Account).</summary>
    public required string Username { get; set; }

    /// <summary>Automation-Secret des Users (NICHT das GUI-Passwort).</summary>
    public required string Secret { get; set; }

    /// <summary>true = https (Standard). Nur im abgeschotteten Lab ggf. false.</summary>
    public bool UseHttps { get; set; } = true;

    /// <summary>
    /// Bei self-signed Zertifikaten (Homelab) auf true setzen, um die
    /// Zertifikatspruefung zu ueberspringen. Produktiv: false lassen.
    /// </summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>
    /// REST-API-Major-Version im Pfad. Ab Checkmk 2.5.0 = "v1"
    /// (kompatibel zum alten "1.0" bis 2.4.0).
    /// </summary>
    public string ApiVersion { get; set; } = "v1";

    /// <summary>Timeout fuer einzelne Requests.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Basis-URL der REST-API, z. B. https://host/site/check_mk/api/v1.</summary>
    public Uri BaseUri
    {
        get
        {
            var scheme = UseHttps ? "https" : "http";
            return new Uri($"{scheme}://{Host}/{Site}/check_mk/api/{ApiVersion}/");
        }
    }
}
