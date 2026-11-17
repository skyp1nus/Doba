using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Doba.App.Motion;
using Doba.App.Services;
using Doba.App.ViewModels;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// One task row of a list (Inbox, Today, Day): checkbox, priority button (click cycles), title (click opens the task),
/// tag + meta in mono 11 px and the optional "→ Today" button. DataContext is a <see cref="TaskRowViewModel"/>.
/// </summary>
/// <remarks>
/// <para><b>Motion.</b> Done: the checkbox fills (TaskCheckBox), the title colour goes Text.Primary → Text.Muted and the
/// strike-through — always present — changes colour from transparent to Text.Muted (both <see cref="M.Strike"/>, ease).
/// Dimmed (tag filter): opacity → <c>Opacity.Dimmed</c> (<see cref="M.Dim"/>). When the row appears for the task in
/// <see cref="TaskActions.PendingEntranceId"/> (just created / moved "→ Today") it plays the new-row entrance
/// (opacity 0→1, translateY −8→0, <see cref="M.NewRow"/>, ease-out) — the task actions are found through the inherited
/// <see cref="EntrancesProperty"/>, which the screen sets on its root.</para>
/// <para>A row re-pointed at another task (virtualised container recycling) shows the new task's state at once; only
/// changes of the same task animate.</para>
/// </remarks>
public partial class TaskListRow : UserControl
{
    /// <summary>The CSS root line height (1.4): a list-row title is 13.5 × 1.4 = 18.9 px tall (compact 13 × 1.4).</summary>
    private const double CssLineHeightFactor = 1.4;

    /// <summary>Colour of the right-hand meta text (time, date, "3 days"). Default Text.Secondary; the Inbox's undated
    /// column uses Text.Muted, as in the prototype.</summary>
    public static readonly StyledProperty<IBrush?> MetaForegroundProperty =
        AvaloniaProperty.Register<TaskListRow, IBrush?>(nameof(MetaForeground));

    /// <summary>Inherited: the <see cref="TaskActions"/> whose pending entrance rows consume. Set it on the screen root.</summary>
    public static readonly AttachedProperty<TaskActions?> EntrancesProperty =
        AvaloniaProperty.RegisterAttached<TaskListRow, Control, TaskActions?>("Entrances", inherits: true);

    private readonly SolidColorBrush _strike = new();
    private readonly Transitions _strikeTransitions;
    private TaskRowViewModel? _row;

    public TaskListRow()
    {
        InitializeComponent();
        _strikeTransitions =
        [
            new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = M.Ms(M.Strike), Easing = M.EaseStd },
        ];
        PART_TitleText.TextDecorations =
        [
            new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = _strike },
        ];
        ActualThemeVariantChanged += (_, _) => ApplyStrike(animate: false);
    }

    /// <summary><c>FontSize × 1.4</c> (CSS <c>line-height: 1.4</c>) for single-line titles.</summary>
    public static IValueConverter CssLineHeight { get; } =
        new FuncValueConverter<double, double>(size => Math.Round(size * CssLineHeightFactor, 3));

    public IBrush? MetaForeground
    {
        get => GetValue(MetaForegroundProperty);
        set => SetValue(MetaForegroundProperty, value);
    }

    public static TaskActions? GetEntrances(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(EntrancesProperty);
    }

    public static void SetEntrances(Control element, TaskActions? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EntrancesProperty, value);
    }

    /// <summary>The strike-through brush (its colour animates; exposed for tests).</summary>
    internal SolidColorBrush StrikeBrush => _strike;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowPropertyChanged;
        }

        _row = DataContext as TaskRowViewModel;
        if (_row is not null)
        {
            _row.PropertyChanged += OnRowPropertyChanged;
        }

        ApplyStrike(animate: false);
        if (IsLoaded)
        {
            TryEntrance();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyStrike(animate: false);
        TryEntrance();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskRowViewModel.Done))
        {
            ApplyStrike(animate: true);
        }
    }

    /// <summary>Strike colour: Text.Muted when done, the same colour fully transparent otherwise (so the fade never
    /// passes through a darker colour).</summary>
    private void ApplyStrike(bool animate)
    {
        var muted = this.TryFindResource("Color.Text.Muted", ActualThemeVariant, out var value) && value is Color c ? c : default;
        var target = _row is { Done: true } ? muted : new Color(0, muted.R, muted.G, muted.B);
        // Without the transitions attached the colour snaps (recycled container, first show, theme change).
        _strike.Transitions = animate ? _strikeTransitions : null;
        _strike.Color = target;
        _strike.Transitions = _strikeTransitions;
    }

    private void TryEntrance()
    {
        if (_row is { } row && GetEntrances(this) is { } actions && actions.ConsumeEntrance(row.Id))
        {
            Animator.SlideFrom(this, 0, M.NewRowY, M.NewRow);
        }
    }
}
