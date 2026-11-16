using Avalonia;

namespace Doba.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppStartup.Begin(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the headless test harness and the previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
