using Checkmk.App.Services;
using Checkmk.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class CsvExporterTests
{
    private static ServiceStatus Svc(string host, string desc, ServiceState state, string output)
        => new() { HostName = host, Description = desc, State = (int)state, PluginOutput = output };

    [Fact]
    public void Writes_header_and_a_simple_row()
    {
        var csv = CsvExporter.ToCsv([Svc("h1", "CPU", ServiceState.Ok, "load 0.5")]);

        csv.Should().StartWith("Host;Alias;Anzeigename;Service;Status;Ausgabe;Ack;Downtime;Age\r\n");
        // Ohne eigenen Anzeigenamen faellt die Spalte auf die Beschreibung zurueck.
        csv.Should().Contain("h1;;CPU;CPU;Ok;load 0.5;nein;nein;");
    }

    [Fact]
    public void Display_name_column_carries_the_speaking_name()
    {
        var csv = CsvExporter.ToCsv([new ServiceStatus
        {
            HostName = "DCC-CMC01",
            Description = "CMCIII-IO3 Input 1",
            DisplayName = "USV Netzausfall (Input 1)",
            State = (int)ServiceState.Ok
        }]);

        csv.Should().Contain("DCC-CMC01;;USV Netzausfall (Input 1);CMCIII-IO3 Input 1;Ok;");
    }

    [Fact]
    public void Quotes_fields_containing_delimiter_or_quotes()
    {
        var csv = CsvExporter.ToCsv([Svc("h1", "Agent", ServiceState.Critical,
            "error 500; retry \"now\"")]);

        // Semikolon + Quote -> gequotet, innere Quotes verdoppelt
        csv.Should().Contain("\"error 500; retry \"\"now\"\"\"");
    }

    [Fact]
    public void Bytes_start_with_utf8_bom()
    {
        var bytes = CsvExporter.ToCsvBytes([Svc("h1", "CPU", ServiceState.Ok, "ok")]);
        bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
    }
}
