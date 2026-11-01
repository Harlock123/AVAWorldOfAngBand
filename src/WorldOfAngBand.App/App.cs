using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Audio;
using WorldOfAngBand.Data;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // "--seed N" plays a given game again; otherwise each start is a new one.
            var args = desktop.Args ?? [];
            var at = Array.IndexOf(args, "--seed");
            var seed = at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out var given) ? given : (ulong)Environment.TickCount64;
            // The game's data with the player's mods, less those switched off (a mod that breaks the data is left out).
            var settings = AppSettings.Read();
            var data = DataLoader.LoadDefault(settings.DisabledMods, out var mods);
            var window = new MainWindow(new DungeonSession(data, seed)) { Settings = settings, ModsLoaded = mods };
            // Sound (the spec's section 8.1): OpenAL with the mixing period chosen (extra large in a virtual machine), and
            // the sound pack with the mods' laid over it; "--mute" stays silent. With no device the game plays on silently.
            var engine = args.Contains("--mute")
                ? new NullAudioEngine()
                : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(settings.Sound.Buffer, VirtualMachine.Detect()));
            var problems = new List<string>();
            var director = new SoundDirector(engine, SoundPack.LoadWithMods(SoundPack.DefaultDirectory, data.ModDirectories, problems));
            director.Apply(settings.Sound);
            window.Sound = director;
            foreach (var problem in problems)
                window.State.Note(problem);
            // A new hero to begin with (closed without choosing, the default Lantern-sworn goes down).
            window.Opened += (_, _) => window.OpenBirth(seed);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
