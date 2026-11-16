using Avalonia;
using Avalonia.Headless;
using Doba.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Doba.App.Tests;

/// <summary>Headless Avalonia with real Skia rendering, so tests can capture PNG frames.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
