using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Doba.App.Shell;

namespace Doba.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.FromStartup();
            var window = ShellComposition.CreateMainWindow(services, this);
            desktop.MainWindow = window;

            // Flush pending writes and close the database on exit (tray / flush-on-hide belong to the Lifecycle package).
            desktop.Exit += (_, _) => services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            AppStartup.WatchFirstFrame(window, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
