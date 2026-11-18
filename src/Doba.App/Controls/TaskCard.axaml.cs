using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Doba.App.Motion;
using Doba.App.Screens.Week;
using Doba.App.ViewModels;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// The week task card (prototype <c>.card</c>, design-reference lines 534–551): a <see cref="Card"/> (Bg.Card, r8,
/// hover Bg.CardHover, padding/min height from <c>Density.CardPadding</c> / <c>Density.CardMinHeight</c>) holding
/// a 16 px <see cref="TaskCheckBox"/> (toggles), the title (13/500, line height 18, clamped to two lines with an
/// ellipsis, breaking between words only, click = edit, always-present strike that fades in when done), and the
/// right-hand meta: priority button (only when set; click = cycle), tag (mono 10.5 Text.Muted) and time
/// (Text.Secondary).
/// <para>DataContext: <see cref="TaskRowViewModel"/>. Completing the task only changes bound properties (check fill,
/// strike and title colour transitions); the card is never rebuilt.</para>
/// <para>Opacity: Opacity.Dimmed when <see cref="TaskRowViewModel.Dimmed"/> (tag filter); <see cref="Faded"/> = the
/// next-week preview look (Opacity.Week.Preview / PreviewDimmed). These state styles are declared once per week row
/// (Screens/Week/WeekRowView.axaml), not per card. Changes fade over Motion.Dim.</para>
/// <para>Keyboard: Space anywhere in the card (checkbox, title, priority) completes the task; Enter keeps the focused
/// button's own action (edit, cycle priority).</para>
/// <para>Entrance: plays the new-row entrance once when it appears for the task in
/// <see cref="TaskListRow.EntrancesProperty"/> (just created / moved), like list rows.</para>
/// </summary>
public partial class TaskCard : UserControl
{
    public static readonly StyledProperty<bool> FadedProperty =
        AvaloniaProperty.Register<TaskCard, bool>(nameof(Faded));

    /// <summary>False while the card is too narrow to keep the tag (see <see cref="WeekCardMeasurer.Room"/>).</summary>
    public static readonly StyledProperty<bool> ShowTagProperty =
        AvaloniaProperty.Register<TaskCard, bool>(nameof(ShowTag), true);

    public TaskCard()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Render as a preview card (week view's next-week cell): Opacity.Week.Preview (PreviewDimmed if also dimmed).</summary>
    public bool Faded
    {
        get => GetValue(FadedProperty);
        set => SetValue(FadedProperty, value);
    }

    /// <summary>Show the tag in the meta (false when it would leave the title less than WeekLayout.MinTitleWidth).</summary>
    public bool ShowTag
    {
        get => GetValue(ShowTagProperty);
        private set => SetValue(ShowTagProperty, value);
    }

    /// <summary>The title text (for measuring and tests).</summary>
    internal StrikeText TitleText => PART_Title;

    protected override Size MeasureOverride(Size availableSize)
    {
        // The same rule the week's "+N more" fitting measures cards with, so both agree on the title width.
        if (!double.IsInfinity(availableSize.Width) && DataContext is TaskRowViewModel row)
        {
            ShowTag = WeekCardMeasurer.Room(availableSize.Width, PART_Card.Padding, PART_Row.ColumnSpacing, row.HasPriority,
                row.HasTag ? row.TagLabel : null, row.HasTime ? row.Time : null).ShowTag;
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FadedProperty)
        {
            PART_Card.Classes.Set("faded", change.GetNewValue<bool>());
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is TaskRowViewModel row && TaskListRow.GetEntrances(this) is { } actions && actions.ConsumeEntrance(row.Id))
        {
            Animator.SlideFrom(this, 0, M.NewRowY, M.NewRow);
        }
    }

    /// <summary>Space on any focused part of the card completes the task (before the title / priority button sees it).</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && DataContext is TaskRowViewModel row
            && row.ToggleCommand.CanExecute(null))
        {
            row.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }
}
