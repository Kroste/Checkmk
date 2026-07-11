using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

public partial class HostSettingsDialog : ChromeWindow
{
    private readonly string _host = "";
    private readonly IHostDomainStore _domainStore = null!;
    private readonly ISshCredentialStore _sshStore = null!;

    public HostSettingsDialog(string host, IHostDomainStore domainStore, ISshCredentialStore sshStore)
    {
        AvaloniaXamlLoader.Load(this);
        _host = host;
        _domainStore = domainStore;
        _sshStore = sshStore;

        this.FindControl<TextBlock>("HostText")!.Text = host;

        // Domain vorbelegen
        var state = _domainStore.Load();
        var entry = state.Hosts.FirstOrDefault(h
            => string.Equals(h.Host, host, StringComparison.OrdinalIgnoreCase));
        this.FindControl<TextBox>("DomainBox")!.Text = entry?.Domain ?? "";

        // SSH-Login vorbelegen
        var creds = _sshStore.Get(host);
        this.FindControl<TextBox>("SshUserBox")!.Text = creds?.User ?? "";
        // Passwort NIE anzeigen — nur setzen wenn User es ueberschreibt.
        // (Wenn eins gespeichert ist, bleibt es unveraendert wenn das Feld leer bleibt.)
    }

    // Parameterloser Ctor fuer XAML-Designer.
    public HostSettingsDialog() => AvaloniaXamlLoader.Load(this);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // Domain speichern (leer -> Eintrag entfernen; damit faellt der Host auf DefaultDomain zurueck).
        var newDomain = this.FindControl<TextBox>("DomainBox")!.Text?.Trim() ?? "";
        var state = _domainStore.Load();
        state.Hosts.RemoveAll(h => string.Equals(h.Host, _host, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(newDomain))
            state.Hosts.Add(new HostDomainEntry { Host = _host, Domain = newDomain });
        _domainStore.Save(state);

        // SSH-Credentials
        var user = this.FindControl<TextBox>("SshUserBox")!.Text?.Trim() ?? "";
        var pw = this.FindControl<TextBox>("SshPasswordBox")!.Text ?? "";

        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pw))
        {
            // Nichts eingetragen -> vorhandenen Eintrag entfernen.
            _sshStore.Remove(_host);
        }
        else
        {
            // Wenn das PW-Feld leer ist, aber bereits eins gespeichert war,
            // behalten wir das gespeicherte — nicht ueberschreiben.
            string? pwToStore = string.IsNullOrEmpty(pw) ? null : pw;
            var existing = _sshStore.Get(_host);
            var keepStored = pwToStore is null && !string.IsNullOrEmpty(existing?.ProtectedPassword);
            if (keepStored)
            {
                // Nur User aktualisieren, Passwort belassen: dazu Get(pw entschluesseln)
                // und wieder speichern — hier greifen wir auf die konkrete Store-Impl zu.
                if (_sshStore is SshCredentialStore s)
                {
                    var oldPw = s.DecryptPassword(existing!);
                    _sshStore.Save(_host, user, oldPw);
                }
                else
                {
                    _sshStore.Save(_host, user, null);
                }
            }
            else
            {
                _sshStore.Save(_host, user, pwToStore);
            }
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
