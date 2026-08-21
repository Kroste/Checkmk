using System.Reflection;
using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Update-Kanal auf einem Ordner statt auf GitHub — bei uns
/// <c>\\samba01\542$\5424_IT-Basis-Dienste\CheckMK\CheckMK_Copilot</c>.
/// </summary>
public class FileShareUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cockpit-share-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Version der Testassembly — gegen die vergleicht der Checker.</summary>
    private static readonly Version Current =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public FileShareUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Best-effort */ }
        GC.SuppressFinalize(this);
    }

    private void Package(string name) => File.WriteAllText(Path.Combine(_dir, name), "ZIP");

    private FileShareUpdateChecker Checker() => new(_dir, new StubPreferences());

    private sealed class StubPreferences : IUpdatePreferences
    {
        public Version? Skipped { get; set; }
        public Version? LoadSkippedVersion() => Skipped;
        public void SaveSkippedVersion(Version version) => Skipped = version;
    }

    /// <summary>Eine Version, die sicher neuer ist als die laufende Assembly.</summary>
    private static string Newer => $"{Current.Major + 9}.0.0";

    // --- Erkennung des Kanaltyps ----------------------------------------

    [Theory]
    [InlineData(@"\\samba01\542$\5424_IT-Basis-Dienste\CheckMK\CheckMK_Copilot")]
    [InlineData(@"C:\updates")]
    [InlineData("//server/share")]
    public void Paths_are_recognised_as_a_folder_channel(string channel)
        => FileShareUpdateChecker.LooksLikeFolder(channel).Should().BeTrue();

    [Theory]
    [InlineData("https://api.github.com/repos/Kroste/Checkmk/releases/latest")]
    [InlineData("http://server/x")]
    [InlineData("")]
    [InlineData(null)]
    public void Addresses_are_not_a_folder_channel(string? channel)
        => FileShareUpdateChecker.LooksLikeFolder(channel).Should().BeFalse();

    // --- Paket finden ----------------------------------------------------

    [Fact]
    public async Task A_newer_package_in_the_folder_is_offered()
    {
        Package($"Checkmk-{Newer}-win-x64.zip");

        var result = await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCheckOutcome.UpdateAvailable);
        result.Info!.Version.Major.Should().Be(Current.Major + 9);
        result.Info.WindowsZipUrl.Should().Contain(_dir);
    }

    [Fact]
    public async Task An_older_package_is_not_offered()
    {
        Package("Checkmk-0.1.0-win-x64.zip");

        (await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken)).Outcome.Should().Be(UpdateCheckOutcome.UpToDate);
    }

    [Fact]
    public async Task The_highest_version_wins_not_the_newest_file()
    {
        // Kopiert jemand ein aelteres Paket zurueck in den Ordner, hat es den
        // neueren Zeitstempel. Nach Datum zu sortieren wuerde daraus ein
        // „Update" auf eine aeltere Version machen.
        Package($"Checkmk-{Current.Major + 9}.0.0-win-x64.zip");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Package($"Checkmk-{Current.Major + 1}.0.0-win-x64.zip");

        var result = await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken);

        result.Info!.Version.Major.Should().Be(Current.Major + 9);
    }

    [Fact]
    public async Task An_empty_folder_is_a_failed_check_not_an_update()
    {
        (await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken)).Outcome.Should().Be(UpdateCheckOutcome.Failed);
    }

    [Fact]
    public async Task An_unreachable_folder_does_not_throw()
    {
        // Notebook ohne Netzlaufwerk ist der Normalfall, nicht die Stoerung.
        var checker = new FileShareUpdateChecker(
            Path.Combine(_dir, "gibtsnicht"), new StubPreferences());

        (await checker.CheckManuallyAsync(TestContext.Current.CancellationToken)).Outcome.Should().Be(UpdateCheckOutcome.Failed);
    }

    [Fact]
    public async Task A_file_without_a_version_in_its_name_is_ignored()
    {
        Package("Checkmk-irgendwas-win-x64.zip");

        (await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken)).Outcome.Should().Be(UpdateCheckOutcome.Failed);
    }

    // --- Manifest als Index ---------------------------------------------

    [Fact]
    public async Task A_manifest_decides_which_package_is_current()
    {
        // Liegen zwei Pakete im Ordner, gibt das Manifest den Ausschlag —
        // sonst koennte ein danebengelegtes ZIP das signierte ueberstimmen.
        Package($"Checkmk-{Newer}-win-x64.zip");
        Package($"Checkmk-{Current.Major + 20}.0.0-win-x64.zip");

        File.WriteAllText(Path.Combine(_dir, "update.json"), UpdateSignature.ToJson(
            new SignedUpdateManifest
            {
                Version = Newer,
                File = $"Checkmk-{Newer}-win-x64.zip",
                Sha256 = "egal", Size = 3, Signature = "egal"
            }));

        var result = await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken);

        result.Info!.Version.Major.Should().Be(Current.Major + 9);
        result.Info.ManifestUrl.Should().EndWith("update.json");
    }

    // --- Release-Notes ---------------------------------------------------

    [Fact]
    public async Task Version_specific_notes_beat_the_collected_file()
    {
        Package($"Checkmk-{Newer}-win-x64.zip");
        File.WriteAllText(Path.Combine(_dir, "RELEASE_NOTES.md"), "Sammeldatei");
        File.WriteAllText(Path.Combine(_dir, $"v{Newer}.md"), "Genau diese Version");

        var result = await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken);

        result.Info!.ReleaseNotes.Should().Be("Genau diese Version");
    }

    [Fact]
    public async Task Missing_notes_do_not_hide_the_update()
    {
        Package($"Checkmk-{Newer}-win-x64.zip");

        var result = await Checker().CheckManuallyAsync(TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCheckOutcome.UpdateAvailable);
        result.Info!.ReleaseNotes.Should().BeEmpty();
    }

    // --- Uebersprungene Version -----------------------------------------

    [Fact]
    public async Task A_skipped_version_is_honoured_only_on_the_automatic_check()
    {
        Package($"Checkmk-{Newer}-win-x64.zip");
        var prefs = new StubPreferences { Skipped = new Version(Current.Major + 9, 0, 0) };
        var checker = new FileShareUpdateChecker(_dir, prefs);

        (await checker.CheckAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        // Wer aktiv prueft, will es wissen — die Uebersprungenheit gilt dort nicht.
        (await checker.CheckManuallyAsync(TestContext.Current.CancellationToken)).Outcome
            .Should().Be(UpdateCheckOutcome.UpdateAvailable);
    }
}
