using Avalonia;
using Doba.App.Platform;
using Doba.App.Services;

namespace Doba.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppStartup.Clock.Start();

        // A command line that is not understood never falls through to the real data folder and Run key.
        if (!StartupFlags.TryParse(args, out var flags, out var error))
        {
            StartupError.Report(error + Environment.NewLine + StartupFlags.Usage, showDialog: false);
            return AppStartup.ExitBadArguments;
        }

        // One process per data folder (demo and self-check read no data and need no claim). A second launch on the
        // same folder brings the running window forward (not for the Run key's --minimized) and ends here, before the
        // database is opened.
        using var instance = flags.DataFolderInUse is { } folder ? SingleInstance.Claim(folder) : null;
        if (instance is { IsPrimary: false })
        {
            if (!flags.Minimized)
            {
                instance.SignalPrimary();
            }

            return 0;
        }

        // Starts loading the database on a pool thread while Avalonia initialises the platform.
        AppStartup.Begin(flags, instance);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (StartupFailedException ex)
        {
            // The data folder could not be opened or read: say so and stop, changing nothing on disk.
            StartupError.Report(ex.Message, showDialog: !flags.Perf);
            return AppStartup.ExitDataUnavailable;
        }
    }

    // Also used by the headless test harness and the previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
