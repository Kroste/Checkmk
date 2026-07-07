using System.Net;
using System.Net.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Checkmk.Core;

public static class CheckmkServiceCollectionExtensions
{
    /// <summary>
    /// Registriert CheckmkClient mit einem eigenen HttpClient. Bei
    /// IgnoreCertificateErrors=true wird die Zertifikatspruefung
    /// deaktiviert (nur fuer self-signed Lab-Setups gedacht).
    /// </summary>
    public static IServiceCollection AddCheckmkClient(
        this IServiceCollection services, Action<CheckmkOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient<CheckmkClient>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<CheckmkOptions>>().Value;
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.All
                };
                if (opts.IgnoreCertificateErrors)
                {
                    handler.ServerCertificateCustomValidationCallback =
                        (_, _, _, _) => true;
                }
                return handler;
            });

        return services;
    }
}
