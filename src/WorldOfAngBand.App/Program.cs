using Avalonia;
using Avalonia.Skia;

namespace WorldOfAngBand.App;

// A classic Program class (not top-level statements) so Windows gets an STA main thread.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        // Every tile and sprite frame is its own texture at the screen's density; Avalonia's default budget is far too
        // small for that (AshenReliquary found the same).
        .With(new SkiaOptions { MaxGpuResourceSizeBytes = 512L * 1024 * 1024 })
        .LogToTrace();
}
