using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NLog;

namespace Checkmk.App.Services;

public enum IpSource
{
    None,
    Ping,
    Dns
}

/// <summary>
/// Ermittelt die IP eines Hosts, wenn Checkmk keine liefert: erst per Ping
/// (loest den Namen auf UND bestaetigt Erreichbarkeit), sonst per DNS-Aufloesung
/// (liefert die IP auch, wenn ICMP geblockt ist). Aufloesung erfolgt vom Rechner,
/// auf dem die App laeuft.
/// </summary>
public static class IpResolver
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static async Task<(string? Ip, IpSource Source)> ResolveAsync(
        string host, int timeoutMs = 1500)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (null, IpSource.None);

        // 1) Ping — loest den Namen auf und prueft Erreichbarkeit.
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            if (reply.Status == IPStatus.Success && reply.Address is not null)
                return (reply.Address.ToString(), IpSource.Ping);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Ping fuer {Host} fehlgeschlagen.", host);
        }

        // 2) DNS — IP auch bei geblocktem ICMP, bevorzugt IPv4.
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            var chosen = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? addrs.FirstOrDefault();
            if (chosen is not null)
                return (chosen.ToString(), IpSource.Dns);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DNS-Aufloesung fuer {Host} fehlgeschlagen.", host);
        }

        return (null, IpSource.None);
    }
}
