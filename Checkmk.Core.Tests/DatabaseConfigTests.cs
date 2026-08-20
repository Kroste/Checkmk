using System.Text.Json;
using Checkmk.Data;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Die ausgelieferte <c>database.json</c> wird von Hand angefasst (Umzug auf
/// einen anderen Server, Testinstanz). Sie muss deshalb beide Schreibweisen
/// vertragen und darf bei einem Tippfehler nicht den Start verhindern.
/// </summary>
public class DatabaseConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cockpit-dbconfig-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Protected_value_wins_over_plain_text()
    {
        // Steht beides drin, gilt der verschleierte Wert — sonst bliebe ein
        // vergessener Klartext-Rest wirksam, den niemand mehr auf dem Schirm hat.
        var config = new DatabaseConnection.DatabaseConfig
        {
            ConnectionString = "Server=alt;",
            ProtectedConnectionString = ConnectionStringObfuscator.Obfuscate("Server=neu;")
        };

        config.Resolve().Should().Be("Server=neu;");
    }

    [Fact]
    public void Plain_text_alone_is_accepted()
    {
        var config = new DatabaseConnection.DatabaseConfig { ConnectionString = "Server=test;" };

        config.Resolve().Should().Be("Server=test;");
    }

    [Fact]
    public void Empty_config_resolves_to_null()
    {
        // null ist ein gueltiger Zustand: ohne Datenbank laeuft das Cockpit
        // mit Cache bzw. Vorgaben weiter.
        new DatabaseConnection.DatabaseConfig().Resolve().Should().BeNull();
        new DatabaseConnection.DatabaseConfig { ConnectionString = "   " }.Resolve().Should().BeNull();
    }

    [Fact]
    public void Written_file_round_trips_and_hides_the_password()
    {
        var path = Path.Combine(_dir, DatabaseConnection.FileName);
        const string plain = "Server=FOC-SQL01;Database=CheckMK_Copilot;Password=geheim;";

        DatabaseConnection.WriteDeployedConfig(path, plain);

        var raw = File.ReadAllText(path);
        raw.Should().NotContain("geheim");
        raw.Should().Contain(ConnectionStringObfuscator.Prefix);

        var parsed = JsonSerializer.Deserialize<DatabaseConnection.DatabaseConfig>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        parsed!.Resolve().Should().Be(plain);
    }

    [Fact]
    public void Property_names_are_matched_case_insensitively()
    {
        // Wer die Datei von Hand schreibt, tippt "connectionString" oder
        // "ConnectionString" — beides muss gehen.
        var parsed = JsonSerializer.Deserialize<DatabaseConnection.DatabaseConfig>(
            """{ "connectionstring": "Server=x;" }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        parsed!.Resolve().Should().Be("Server=x;");
    }
}
