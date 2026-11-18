using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Doba.App.Shell;

namespace Doba.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Grayscale text, as in the prototype (-webkit-font-smoothing: antialiased): every top level (windows, popups)
        // gets it before its first frame and its visuals inherit it. TextOptions is not a styled property, so a
        // ControlTheme setter cannot carry it. No hinting either: Chromium draws the outlines unhinted, and the default
        // (light) hinting stretches small glyphs to whole pixels (the ASAP "!" came out a pixel taller).
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TopLevel>((topLevel, _) =>
        {
            TextOptions.SetTextRenderingMode(topLevel, TextRenderingMode.Antialias);
            TextOptions.SetTextHintingMode(topLevel, TextHintingMode.None);
        });
    }

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
