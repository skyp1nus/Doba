using Avalonia;
using Avalonia.Controls;
using Doba.App.Screens;

namespace Doba.App.Shell;

/// <summary>
/// The main panel's body. Creates a screen's view on its first visit (<see cref="ScreenFactory.CreateView"/>) and keeps
/// it; switching screens only changes which cached view is visible (hidden views are not measured or rendered).
/// Exactly one view is visible: the one for <see cref="Screen"/>.
/// </summary>
public sealed class ScreenHost : Panel
{
    public static readonly StyledProperty<ScreenViewModel?> ScreenProperty =
        AvaloniaProperty.Register<ScreenHost, ScreenViewModel?>(nameof(Screen));

    private readonly Dictionary<ScreenViewModel, Control> _views = [];
    private Control? _visible;

    /// <summary>The screen to show (bound to <c>MainViewModel.Current</c>).</summary>
    public ScreenViewModel? Screen
    {
        get => GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    /// <summary>The visible screen view.</summary>
    public Control? VisibleView => _visible;

    /// <summary>The cached view of <paramref name="screen"/>, or null if it has not been shown yet.</summary>
    public Control? ViewOf(ScreenViewModel screen) => _views.GetValueOrDefault(screen);

    /// <summary>Number of screen views created so far.</summary>
    public int CachedViewCount => _views.Count;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScreenProperty)
        {
            ShowScreen(change.GetNewValue<ScreenViewModel?>());
        }
    }

    private void ShowScreen(ScreenViewModel? screen)
    {
        Control? view = null;
        if (screen is not null && !_views.TryGetValue(screen, out view))
        {
            view = ScreenFactory.CreateView(screen);
            view.DataContext = screen;
            _views[screen] = view;
            Children.Add(view);
        }

        if (ReferenceEquals(view, _visible))
        {
            return;
        }

        if (_visible is not null)
        {
            _visible.IsVisible = false;
        }

        if (view is not null)
        {
            view.IsVisible = true;
        }

        _visible = view;
    }
}
