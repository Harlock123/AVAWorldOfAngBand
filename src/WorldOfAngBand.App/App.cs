using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
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
            var window = new MainWindow(new DungeonSession(DataLoader.LoadDefault(), seed));
            // A new hero to begin with (closed without choosing, the default Lantern-sworn goes down).
            window.Opened += (_, _) => window.OpenBirth(seed);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
