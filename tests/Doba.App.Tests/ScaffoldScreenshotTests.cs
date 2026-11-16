using Avalonia.Headless.XUnit;

namespace Doba.App.Tests;

public class ScaffoldScreenshotTests
{
    [AvaloniaFact]
    public void Main_window_renders_to_png()
    {
        var window = new MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        var path = Screenshots.Capture(window, "_scaffold");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
