namespace Checkmk.PluginContracts.Services;

/// <summary>Ergebnis einer Agent-Update-Ausfuehrung.</summary>
public sealed record AgentUpdateResult(bool Success, string Output);

/// <summary>
/// Contract fuer das Checkmk-Agent-Update. Implementiert vom
/// AgentUpdater-Plugin, konsumierbar von anderen Plugins (z. B. dem
/// vSphere-Baseimage-Plugin, das Reihenaktualisierungen fahren will).
///
/// Windows-only (WinRM/PowerShell-Remoting). Nicht-Windows-Aufrufer
/// bekommen ein <c>Success = false</c> mit Erklaerung im Output.
/// </summary>
public interface IAgentUpdater
{
    /// <summary>
    /// Fuehrt die Agent-Update-Skript-Vorlage auf dem Zielhost per Remote-
    /// PowerShell aus.
    /// </summary>
    /// <param name="hostName">Ziel-Host (Kurzname oder FQDN).</param>
    /// <param name="user">Admin-Benutzer fuer die Remote-Session (DOMAENE\User).</param>
    /// <param name="password">Passwort in Klartext, nur runtime-mem.</param>
    /// <param name="agentShare">UNC-Pfad zum Ordner mit dem MSI.</param>
    /// <param name="scriptTemplate">PowerShell-Skript-Vorlage mit {host} und
    /// {installer}-Platzhaltern.</param>
    /// <param name="progress">Optionaler Fortschritts-Callback fuer
    /// Zeilen-Ausgabe (Log-Anzeige im UI).</param>
    /// <param name="ct">Cancellation.</param>
    Task<AgentUpdateResult> UpdateAsync(
        string hostName,
        string user,
        string password,
        string agentShare,
        string scriptTemplate,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
