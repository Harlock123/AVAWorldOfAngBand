using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WorldOfAngBand.App.Tests.TestAppBuilder))]
// One window at a time: the tests share the UI thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WorldOfAngBand.App.Tests;

public static class TestAppBuilder
{
    // Real Skia drawing (not the stub renderer) so tests can capture and look at frames.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
