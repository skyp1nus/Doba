using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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
/// <see cref="StrikeText"/> strike-through — always present, a crisp 1 px line — changes colour from transparent to
/// Text.Muted (both <see cref="M.Strike"/>, ease).
/// Dimmed (tag filter): opacity → <c>Opacity.Dimmed</c> (<see cref="M.Dim"/>). When the row appears for the task in
/// <see cref="TaskActions.PendingEntranceId"/> (just created / moved "→ Today") it plays the new-row entrance
/// (opacity 0→1, translateY −8→0, <see cref="M.NewRow"/>, ease-out) — the task actions are found through the inherited
/// <see cref="EntrancesProperty"/>, which the screen sets on its root.</para>
/// <para>A row re-pointed at another task (virtualised container recycling) shows the new task's state at once — dim,
/// checkbox, title colour and strike (<see cref="SnapScope"/> around the data-context change); only changes of the same
/// task animate.</para>
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

    private TaskRowViewModel? _row;

    /// <summary>Open while the data context changes on a shown row.</summary>
    private SnapScope? _snap;

    /// <summary>Space is down and has already toggled the task (key repeat does not toggle again).</summary>
    private bool _spaceDown;

    public TaskListRow()
    {
        InitializeComponent();
        // Tunnel: a Button marks Space handled on its own KeyDown, so a bubbling handler would never see it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
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

    /// <summary>The title text with its strike (null until the title button is templated; for tests).</summary>
    internal StrikeText? TitleText => PART_Title.Presenter?.Child as StrikeText;

    /// <summary>
    /// A shown row re-pointed at another task (virtualised container recycling) shows the new task's state at once:
    /// the row's dim, the checkbox fill and the title colour and strike do not fade between the two tasks' states.
    /// </summary>
    protected override void OnDataContextBeginUpdate()
    {
        base.OnDataContextBeginUpdate();
        if (IsLoaded && _snap is null)
        {
            _snap = SnapScope.Open(this);
        }
    }

    protected override void OnDataContextEndUpdate()
    {
        base.OnDataContextEndUpdate();
        _snap?.Dispose();
        _snap = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _row = DataContext as TaskRowViewModel;
        PART_Title.Content = _row;
        _spaceDown = false;
        if (IsLoaded)
        {
            TryEntrance();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TryEntrance();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _spaceDown = false;
    }

    /// <summary>
    /// KICKOFF §6: Space completes the task whose row has focus — from the title, the priority icon or "→ Today" too,
    /// whose buttons would otherwise click on Space (open the edit dialog, cycle the priority, move the task). The
    /// checkbox keeps its own Space handling (same effect). Enter still activates the focused button.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || _row is null || IsFromCheckBox(e.Source))
        {
            return;
        }

        e.Handled = true;
        if (!_spaceDown)
        {
            _spaceDown = true;
            _row.ToggleCommand.Execute(null);
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceDown)
        {
            _spaceDown = false;
            e.Handled = true;
        }
    }

    private bool IsFromCheckBox(object? source) =>
        source is Visual v && (ReferenceEquals(v, PART_Check) || PART_Check.IsVisualAncestorOf(v));

    private void TryEntrance()
    {
        if (_row is { } row && GetEntrances(this) is { } actions && actions.ConsumeEntrance(row.Id))
        {
            Animator.SlideFrom(this, 0, M.NewRowY, M.NewRow);
        }
    }
}
