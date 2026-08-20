using System.Net;
using System.Text;
using Checkmk.Core;
using Checkmk.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der Service-Abruf wird gestreamt statt in einen String gelesen — sonst lief
/// das Deserialisieren zehntausender Checks nach dem <c>await</c> wieder auf dem
/// UI-Thread und die App stand mehrere Sekunden. Der Byte-Fortschritt aus
/// demselben Stream speist den Balken in der Statusleiste.
/// </summary>
public class ServiceStatusStreamingTests
{
    [Fact]
    public async Task Streaming_get_reports_byte_progress()
    {
        // Genug Services, damit die Drosselung im CountingStream (alle 256 KB)
        // mindestens einmal auslaest — ein kleiner Body meldet nur am Ende.
        var client = Make(ServicesJson(count: 4000), out _);
        var reports = new List<TransferProgress>();

        var services = await client.GetServiceStatusesAsync(
            filter: null,
            ct: TestContext.Current.CancellationToken,
            progress: new CollectingProgress(reports));

        services.Should().HaveCount(4000);
        reports.Should().NotBeEmpty("der Balken braucht Zwischenstaende, nicht nur das Endergebnis");
        reports.Select(r => r.BytesRead).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Progress_fraction_is_null_without_content_length()
    {
        // Checkmk schickt die grossen Livestatus-Antworten chunked. Ohne
        // Content-Length darf kein Prozentwert erfunden werden — der Aufrufer
        // schaetzt dann aus dem letzten Lauf oder zeigt einen Marquee-Balken.
        var progress = new TransferProgress(BytesRead: 1024, TotalBytes: null);

        progress.Fraction.Should().BeNull();
        new TransferProgress(512, 1024).Fraction.Should().Be(0.5);
    }

    [Fact]
    public async Task Cancellation_aborts_the_download()
    {
        var client = Make(ServicesJson(count: 100), out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => client.GetServiceStatusesAsync(filter: null, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Unparseable_answer_keeps_a_diagnostic_snippet()
    {
        // Beim Streamen gibt es keinen vollstaendigen Body mehr fuer die
        // Fehlermeldung. Der mitgeschnittene Kopf muss reichen, um z. B. eine
        // HTML-Loginseite vom Proxy zu erkennen.
        var client = Make("<html><body>Proxy Authentication Required</body></html>", out _);

        var act = () => client.GetServiceStatusesAsync(
            filter: null, ct: TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<CheckmkApiException>();
        ex.Which.ResponseBody.Should().Contain("Proxy Authentication Required");
    }

    private static string ServicesJson(int count)
    {
        var sb = new StringBuilder("{\"value\":[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"extensions\":{")
              .Append("\"host_name\":\"host").Append(i % 200).Append("\",")
              .Append("\"description\":\"Service ").Append(i).Append("\",")
              .Append("\"state\":0,")
              .Append("\"plugin_output\":\"OK - alles gut, aber mit genug Text fuer ein paar Kilobyte\",")
              .Append("\"acknowledged\":0,\"scheduled_downtime_depth\":0")
              .Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static CheckmkClient Make(string body, out StubHandler handler)
    {
        handler = new StubHandler(body);
        var http = new HttpClient(handler);
        return new CheckmkClient(http, new CheckmkOptions
        {
            Host = "monitoring.test",
            Site = "prod",
            Username = "automation",
            Secret = "s3cr3t"
        });
    }

    /// <summary>Antwortet ohne Content-Length — wie Checkmk bei grossen Antworten.</summary>
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class CollectingProgress(List<TransferProgress> sink) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value)
        {
            lock (sink) sink.Add(value);
        }
    }
}
