using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Doba.App.Shell;

/// <summary>
/// The 40 px title bar. Marks the Windows non-client roles with <see cref="WindowDecorationProperties.SetElementRole"/>
/// (effective with <c>ExtendClientAreaToDecorationsHint</c>): the bar is the drag area (TitleBar: move, double-click
/// maximise, snap layouts on the maximise button), the caption buttons are Minimize / Maximize / Close, the search box is
/// an interactive User element. The Click handlers cover platforms where the roles are only interactive elements.
/// </summary>
public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
        WindowDecorationProperties.SetElementRole(PART_DragArea, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(PART_Search, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(PART_Minimize, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(PART_Maximize, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(PART_Close, WindowDecorationsElementRole.CloseButton);
    }

    internal Control DragArea => PART_DragArea;

    internal Button SearchButton => PART_Search;

    internal Button MinimizeButton => PART_Minimize;

    internal Button MaximizeButton => PART_Maximize;

    internal Button CloseButton => PART_Close;

    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } w)
        {
            w.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}
