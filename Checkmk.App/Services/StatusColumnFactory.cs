using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Checkmk.App.Converters;
using Checkmk.Core.Models;

namespace Checkmk.App.Services;

/// <summary>
/// Baut die Spalten der Service-Tabelle aus den Schluesseln eines
/// <see cref="ViewerProfile"/>. Die Schluessel sind bewusst die Namen aus den
/// Checkmk-Sichten (<c>host</c>, <c>service_description</c>, <c>svc_state_age</c> …),
/// damit man eine vorhandene Web-Sicht 1:1 abschreiben kann; dazu kommen ein paar
/// Cockpit-Eigene (<c>state_dot</c>, <c>host_alias</c>).
/// <para>
/// Wird nur im Viewer-Modus benutzt — ohne <c>viewer.json</c> bleibt das in
/// <c>StatusView.axaml</c> deklarierte Standard-Grid unangetastet.
/// </para>
/// </summary>
public static class StatusColumnFactory
{
    private sealed record ColumnSpec(string Header, Func<DataGridColumn> Create);

    private static readonly IReadOnlyDictionary<string, ColumnSpec> Specs =
        new Dictionary<string, ColumnSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["state_dot"] = new("", () => DotColumn()),
            ["host"] = new("Host", () => Text("Host", nameof(ServiceStatus.HostName), 160)),
            ["host_alias"] = new("Alias", () => Text("Alias", nameof(ServiceStatus.HostAlias), 180)),
            ["service_display_name"] = new("Anzeigename",
                () => Text("Anzeigename", nameof(ServiceStatus.DisplayNameOrDescription), 200)),
            ["service_description"] = new("Service",
                () => Text("Service", nameof(ServiceStatus.Description), 200)),
            ["service_state"] = new("Status",
                () => Text("Status", nameof(ServiceStatus.ServiceState), 90)),
            ["service_plugin_output"] = new("Ausgabe",
                () => Star("Ausgabe", nameof(ServiceStatus.PluginOutput))),
            ["svc_acknowledged"] = new("Ack",
                () => Check("Ack", nameof(ServiceStatus.IsAcknowledged))),
            ["svc_in_downtime"] = new("DT",
                () => Check("DT", nameof(ServiceStatus.InDowntime))),
            // Bewusst OHNE Alters-Einfaerbung: bei svc_check_age ist "frisch" gut und
            // "alt" schlecht — genau umgekehrt zu svc_state_age, wofuer AgeToBrush
            // gebaut ist. Rot fuer einen Check, der gerade eben lief, waere irrefuehrend.
            ["svc_check_age"] = new("Letzter Check",
                () => Text("Letzter Check", nameof(ServiceStatus.CheckAge), 110,
                    nameof(ServiceStatus.LastCheckUnix))),
            ["svc_state_age"] = new("Alter Status",
                () => AgeColumn("Alter Status", nameof(ServiceStatus.Age),
                    nameof(ServiceStatus.LastStateChange), nameof(ServiceStatus.LastStateChangeUnix)))
        };

    /// <summary>Alle unterstuetzten Schluessel — fuer Logmeldungen und Doku.</summary>
    public static IReadOnlyCollection<string> KnownKeys { get; } = [.. Specs.Keys];

    public static bool IsKnown(string key) => Specs.ContainsKey(key);

    /// <summary>
    /// Erzeugt die Spalten in der Reihenfolge der uebergebenen Schluessel.
    /// Unbekannte Schluessel sind bereits beim Laden des Profils aussortiert worden;
    /// falls doch einer durchrutscht, wird er hier still uebersprungen.
    /// </summary>
    public static IReadOnlyList<DataGridColumn> Build(IEnumerable<string> keys)
    {
        var columns = new List<DataGridColumn>();
        foreach (var key in keys)
        {
            if (Specs.TryGetValue(key, out var spec))
                columns.Add(spec.Create());
        }
        return columns;
    }

    // --- Spaltentypen ----------------------------------------------------

    /// <summary><paramref name="sortPath"/> setzen, wenn der angezeigte Text nicht
    /// in seiner eigenen Reihenfolge sortiert werden darf (z. B. "3 h" vs. "5 m").</summary>
    private static DataGridTextColumn Text(string header, string path, double width,
        string? sortPath = null) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
        SortMemberPath = sortPath ?? path
    };

    private static DataGridTextColumn Star(string header, string path) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
    };

    private static DataGridCheckBoxColumn Check(string header, string path) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(50)
    };

    /// <summary>Ampelpunkt wie im XAML-Standardgrid.</summary>
    private static DataGridTemplateColumn DotColumn() => new()
    {
        Header = "",
        Width = new DataGridLength(34),
        CanUserSort = true,
        SortMemberPath = nameof(ServiceStatus.State),
        CellTemplate = new FuncDataTemplate<ServiceStatus>((_, _) => new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new Binding(nameof(ServiceStatus.ServiceState))
            {
                Converter = StateToBrushConverter.Instance
            }
        })
    };

    /// <summary>
    /// Alters-Spalte: kompakter Text ("3 h 12 m") eingefaerbt nach Frische.
    /// <paramref name="sortPath"/> zeigt auf den Unix-Zeitstempel — sonst wuerde
    /// die Tabelle den formatierten String alphabetisch sortieren ("3 h" &lt; "5 m").
    /// </summary>
    private static DataGridTemplateColumn AgeColumn(
        string header, string textPath, string brushPath, string sortPath) => new()
    {
        Header = header,
        Width = new DataGridLength(110),
        CanUserSort = true,
        SortMemberPath = sortPath,
        CellTemplate = new FuncDataTemplate<ServiceStatus>((_, _) => new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0),
            [!TextBlock.TextProperty] = new Binding(textPath),
            [!TextBlock.ForegroundProperty] = new Binding(brushPath)
            {
                Converter = AgeToBrushConverter.Instance
            }
        })
    };
}
