using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkmk.Core.Exceptions;
using Checkmk.Core.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace Checkmk.Core;

/// <summary>
/// Typisierter Client fuer die Checkmk REST-API (Version v1, Checkmk 2.5.x).
///
/// Auth: Bearer-Header im Checkmk-Format "Bearer {user} {secret}"
/// (Username und Secret durch ein Leerzeichen getrennt).
///
/// Wichtig: Der HTTP-Statuscode bestaetigt nur die Uebertragung, nicht die
/// fachliche Ausfuehrung. Kommandos (Downtime/Ack) werden serverseitig via
/// Livestatus verarbeitet -> bei Bedarf danach den Zustand erneut abfragen.
/// </summary>
public sealed class CheckmkClient
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // WhenWritingNull: Checkmk lehnt null-Werte im "attributes"-Block ab
    // ("These fields have problems: attributes"). Nicht gesetzte Attribute
    // muessen weggelassen, nicht als null gesendet werden.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public CheckmkClient(HttpClient http, IOptions<CheckmkOptions> options)
        : this(http, options.Value) { }

    public CheckmkClient(HttpClient http, CheckmkOptions options)
    {
        _http = http;
        _http.BaseAddress = options.BaseUri;
        _http.Timeout = options.Timeout;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"{options.Username} {options.Secret}");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Secret niemals im Klartext loggen.
        Log.Debug("CheckmkClient initialisiert fuer {BaseUri} (User={User}, Secret={Secret})",
            options.BaseUri, options.Username, Mask(options.Secret));
    }

    // ---------------------------------------------------------------------
    // Read: Version / Setup / Livestatus
    // ---------------------------------------------------------------------

    /// <summary>GET /version — praktisch zum Verbindungstest und Editions-Check.</summary>
    public Task<CheckmkVersionInfo> GetVersionAsync(CancellationToken ct = default)
        => GetAsync<CheckmkVersionInfo>("version", ct);

    /// <summary>Alle konfigurierten Hosts (Setup-Seite).</summary>
    public async Task<IReadOnlyList<CheckmkObject<HostConfigExtensions>>> GetHostConfigsAsync(
        bool effectiveAttributes = false, CancellationToken ct = default)
    {
        var url = $"domain-types/host_config/collections/all?effective_attributes={effectiveAttributes.ToString().ToLowerInvariant()}";
        var result = await GetAsync<CheckmkCollection<CheckmkObject<HostConfigExtensions>>>(url, ct);
        return result.Value;
    }

    /// <summary>Live-Status aller Hosts (Monitoring/Livestatus).</summary>
    public async Task<IReadOnlyList<HostStatus>> GetHostStatusesAsync(CancellationToken ct = default)
    {
        var cols = new[] { "name", "state", "plugin_output", "acknowledged", "scheduled_downtime_depth" };
        var url = "domain-types/host/collections/all?" + ColumnsQuery(cols, hostNameCol: "name");
        var result = await GetAsync<CheckmkCollection<HostStatusEnvelope>>(url, ct);
        return result.Value.Select(v => v.Extensions).ToList();
    }

    /// <summary>
    /// Live-Status von Services. Optional auf einen Host gefiltert
    /// (Livestatus-Query ueber host_name).
    /// </summary>
    public async Task<IReadOnlyList<ServiceStatus>> GetServiceStatusesAsync(
        string? hostName = null, CancellationToken ct = default)
    {
        var cols = new[]
        {
            "host_name", "description", "state", "plugin_output",
            "acknowledged", "scheduled_downtime_depth", "last_check"
        };
        var url = "domain-types/service/collections/all?" + ColumnsQuery(cols);

        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var query = JsonSerializer.Serialize(new
            {
                op = "=",
                left = "host_name",
                right = hostName
            });
            url += "&query=" + Uri.EscapeDataString(query);
        }

        var result = await GetAsync<CheckmkCollection<ServiceStatusEnvelope>>(url, ct);
        return result.Value.Select(v => v.Extensions).ToList();
    }

    // ---------------------------------------------------------------------
    // Write: Host anlegen / Ack / Downtime / Changes aktivieren
    // ---------------------------------------------------------------------

    /// <summary>Legt einen Host an (Setup). Vergiss nicht ActivateChangesAsync danach.</summary>
    public async Task CreateHostAsync(string hostName, string folder = "/",
        HostAttributes? attributes = null, CancellationToken ct = default)
    {
        var payload = new
        {
            folder,
            host_name = hostName,
            attributes = attributes ?? new HostAttributes()
        };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/host_config/collections/all", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Acknowledged ein Host-Problem.</summary>
    public async Task AcknowledgeHostProblemAsync(string hostName, string comment,
        bool sticky = true, bool notify = true, bool persistent = false,
        CancellationToken ct = default)
    {
        var payload = new
        {
            acknowledge_type = "host",
            host_name = hostName,
            sticky,
            notify,
            persistent,
            comment
        };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/acknowledge/collections/host", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Acknowledged ein einzelnes Service-Problem.</summary>
    public async Task AcknowledgeServiceProblemAsync(string hostName, string serviceDescription,
        string comment, bool sticky = true, bool notify = true, bool persistent = false,
        CancellationToken ct = default)
    {
        var payload = new
        {
            acknowledge_type = "service",
            host_name = hostName,
            service_description = serviceDescription,
            sticky,
            notify,
            persistent,
            comment
        };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/acknowledge/collections/service", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Plant eine Host-Downtime.</summary>
    public async Task ScheduleHostDowntimeAsync(string hostName, DateTimeOffset start,
        DateTimeOffset end, string comment, CancellationToken ct = default)
    {
        var payload = new
        {
            downtime_type = "host",
            host_name = hostName,
            start_time = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            end_time = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            recur = "fixed",
            duration = 0,
            comment
        };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/downtime/collections/host", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Plant eine Downtime fuer einen einzelnen Service.</summary>
    public async Task ScheduleServiceDowntimeAsync(string hostName, string serviceDescription,
        DateTimeOffset start, DateTimeOffset end, string comment, CancellationToken ct = default)
    {
        var payload = new
        {
            downtime_type = "service",
            host_name = hostName,
            service_descriptions = new[] { serviceDescription },
            start_time = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            end_time = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            recur = "fixed",
            duration = 0,
            comment
        };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/downtime/collections/service", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // ---------------------------------------------------------------------
    // Service Discovery
    // ---------------------------------------------------------------------

    /// <summary>
    /// Startet einen Service-Discovery-Run auf dem gegebenen Host. Laeuft als
    /// Hintergrund-Task auf dem Server — mit <see cref="WaitForServiceDiscoveryAsync"/>
    /// pollen bis fertig, danach <see cref="ActivateChangesAsync"/> aufrufen.
    /// </summary>
    public async Task StartServiceDiscoveryAsync(string hostName,
        string mode = ServiceDiscoveryMode.FixAll, CancellationToken ct = default)
    {
        var payload = new { host_name = hostName, mode };
        using var resp = await _http.PostAsJsonAsync(
            "domain-types/service_discovery_run/actions/start/invoke", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Aktueller Status eines laufenden Discovery-Runs.</summary>
    public async Task<ServiceDiscoveryRunState> GetServiceDiscoveryRunAsync(string hostName,
        CancellationToken ct = default)
    {
        var envelope = await GetAsync<CheckmkObject<ServiceDiscoveryRunState>>(
            $"objects/service_discovery_run/{Uri.EscapeDataString(hostName)}", ct);
        return envelope.Extensions ?? new ServiceDiscoveryRunState();
    }

    /// <summary>
    /// Pollt <see cref="GetServiceDiscoveryRunAsync"/> bis der Run abgeschlossen ist
    /// (<c>active == false</c>). Standard-Timeout 2 Minuten, Poll-Intervall 1.5 s.
    /// </summary>
    public async Task WaitForServiceDiscoveryAsync(string hostName,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        var delay = pollInterval ?? TimeSpan.FromMilliseconds(1500);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var state = await GetServiceDiscoveryRunAsync(hostName, ct);
            if (!state.Active)
                return;
            await Task.Delay(delay, ct);
        }
        throw new TimeoutException(
            $"Service-Discovery fuer Host '{hostName}' hat das Zeitlimit ueberschritten.");
    }

    /// <summary>Convenience: Discovery starten und auf Ende warten (kombiniert Start + Wait).</summary>
    public async Task DiscoverServicesAsync(string hostName,
        string mode = ServiceDiscoveryMode.FixAll,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await StartServiceDiscoveryAsync(hostName, mode, ct);
        await WaitForServiceDiscoveryAsync(hostName, timeout, ct: ct);
    }

    // ---------------------------------------------------------------------
    // Activate Changes
    // ---------------------------------------------------------------------

    /// <summary>
    /// Aktiviert ausstehende Aenderungen (Setup -> scharfschalten).
    /// If-Match: * erspart das vorherige ETag-Abholen.
    /// </summary>
    public async Task ActivateChangesAsync(bool forceForeignChanges = false,
        CancellationToken ct = default)
    {
        var payload = new
        {
            redirect = false,
            force_foreign_changes = forceForeignChanges,
            sites = Array.Empty<string>()
        };
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "domain-types/activation_run/actions/activate-changes/invoke")
        {
            Content = JsonContent.Create(payload, options: JsonOpts)
        };
        req.Headers.TryAddWithoutValidation("If-Match", "*");

        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativeUrl, ct);
        await EnsureSuccessAsync(resp, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOpts)
                ?? throw new CheckmkApiException("Antwort war leer/null.", resp.StatusCode, body);
        }
        catch (JsonException ex)
        {
            throw new CheckmkApiException(
                $"Antwort konnte nicht deserialisiert werden: {ex.Message}",
                resp.StatusCode, body, ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        var detail = TryExtractProblemDetail(body) ?? resp.ReasonPhrase ?? "Unbekannter Fehler";

        Log.Warn("Checkmk API {Status}: {Detail}", (int)resp.StatusCode, detail);

        throw new CheckmkApiException(
            $"Checkmk API antwortete {(int)resp.StatusCode} ({resp.StatusCode}): {detail}",
            resp.StatusCode, body);
    }

    /// <summary>Zieht "title"/"detail" aus einer RFC7807 problem+json-Antwort.</summary>
    private static string? TryExtractProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return (title, detail) switch
            {
                (not null, not null) => $"{title} — {detail}",
                (not null, null) => title,
                (null, not null) => detail,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ColumnsQuery(IEnumerable<string> columns, string? hostNameCol = null)
        => string.Join("&", columns.Select(c => "columns=" + Uri.EscapeDataString(c)));

    /// <summary>Maskiert ein Secret fuer Logausgaben (nur erste/letzte 2 Zeichen).</summary>
    private static string Mask(string secret)
        => secret.Length <= 4
            ? new string('*', secret.Length)
            : $"{secret[..2]}{new string('*', secret.Length - 4)}{secret[^2..]}";

    // Interne Envelopes: Livestatus-Endpunkte packen die Spalten in "extensions".
    private sealed record HostStatusEnvelope(HostStatus Extensions);
    private sealed record ServiceStatusEnvelope(ServiceStatus Extensions);
}
