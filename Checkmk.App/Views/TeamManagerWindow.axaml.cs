using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Views;

/// <summary>
/// Teams anlegen und Anmeldungen zuordnen.
///
/// Teams sind <b>Organisation, kein Zugriffsschutz</b> — alle 48 Personen dürfen
/// ohnehin alle Hosts sehen. Sie bündeln geteilte Filter, damit die
/// Urlaubsvertretung nicht bei null anfängt.
/// </summary>
public partial class TeamManagerWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ITeamStore? _teams;

    /// <summary>true, wenn etwas geändert wurde — der Aufrufer baut dann seine
    /// Auswahllisten neu.</summary>
    public bool Changed { get; private set; }

    public TeamManagerWindow(ITeamStore teams)
    {
        AvaloniaXamlLoader.Load(this);
        _teams = teams;

        this.FindControl<ListBox>("TeamList")!.SelectionChanged += (_, _) => LoadSelected();
        Reload();
    }

    // Parameterloser ctor fuer XAML-Designer.
    public TeamManagerWindow() => AvaloniaXamlLoader.Load(this);

    private ListBox List => this.FindControl<ListBox>("TeamList")!;
    private TeamRow? Selected => List.SelectedItem as TeamRow;

    private void Reload(int? select = null)
    {
        if (_teams is null) return;

        var rows = _teams.Current.Teams;
        List.ItemsSource = rows;
        List.SelectedItem = select is { } id
            ? rows.FirstOrDefault(t => t.TeamId == id)
            : rows.FirstOrDefault();
        LoadSelected();
    }

    private void LoadSelected()
    {
        var t = Selected;
        this.FindControl<StackPanel>("Editor")!.IsEnabled = t is not null;
        this.FindControl<Button>("DeleteButton")!.IsEnabled = t is not null;

        this.FindControl<TextBox>("NameBox")!.Text = t?.Name ?? "";
        this.FindControl<TextBox>("DescriptionBox")!.Text = t?.Description ?? "";
        this.FindControl<TextBox>("MembersBox")!.Text =
            t is null ? "" : string.Join(Environment.NewLine, t.Members);
    }

    private void Status(string message)
        => this.FindControl<TextBlock>("StatusText")!.Text = message;

    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (_teams is null) return;
        await Guarded(async () =>
        {
            var name = NextName();
            var id = await _teams.CreateAsync(name, null);
            Changed = true;
            Reload(id);
            Status($"Team „{name}“ angelegt.");
        });
    }

    private string NextName()
    {
        var existing = _teams?.Current.Teams.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? [];
        var i = 1;
        while (existing.Contains($"Team {i}")) i++;
        return $"Team {i}";
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_teams is null || Selected is not { } team) return;

        var name = this.FindControl<TextBox>("NameBox")!.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Status("Ein Team braucht einen Namen.");
            return;
        }

        var members = (this.FindControl<TextBox>("MembersBox")!.Text ?? "")
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        await Guarded(async () =>
        {
            await _teams.RenameAsync(team.TeamId, name,
                this.FindControl<TextBox>("DescriptionBox")!.Text);
            await _teams.SetMembersAsync(team.TeamId, members);
            Changed = true;
            Reload(team.TeamId);
            Status($"„{name}“ gespeichert — {members.Count} Mitglied(er).");
        });
    }

    /// <summary>
    /// Löschen nimmt die geteilten Filter des Teams mit. Anders als beim
    /// Bereichsbaum ist das hier richtig — ein Filter ohne Team gehört
    /// niemandem und ist in der Datenbank gar nicht erlaubt. Deshalb wird die
    /// Zahl vorher genannt, statt still zu löschen.
    /// </summary>
    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_teams is not TeamStore store || Selected is not { } team) return;

        await Guarded(async () =>
        {
            var count = await store.CountFiltersAsync(team.TeamId);
            if (count > 0 && !_deleteConfirmed.Contains(team.TeamId))
            {
                _deleteConfirmed.Add(team.TeamId);
                Status($"„{team.Name}“ hat {count} geteilte(n) Filter. "
                     + "Noch einmal „Löschen“ klicken, um Team und Filter zu entfernen.");
                return;
            }

            await store.DeleteAsync(team.TeamId);
            _deleteConfirmed.Remove(team.TeamId);
            Changed = true;
            Reload();
            Status($"Team „{team.Name}“ gelöscht.");
        });
    }

    private readonly HashSet<int> _deleteConfirmed = [];

    /// <summary>
    /// Ein Schreibfehler darf den Dialog nicht beenden. Genau das ist schon
    /// einmal passiert: eine Ausnahme aus einem RelayCommand lief in den
    /// Avalonia-Dispatcher und riss den Prozess mit.
    /// </summary>
    private async Task Guarded(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Log.Warn(ex, "Team-Verwaltung: Vorgang fehlgeschlagen.");
            Status($"Fehlgeschlagen: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(Changed);
}
