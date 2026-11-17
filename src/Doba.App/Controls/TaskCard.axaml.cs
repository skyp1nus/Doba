using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.App.ViewModels;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Controls;

/// <summary>
/// Who decides whether a freshly realized task visual plays the new-row entrance (motion spec: opacity 0→1,
/// translateY −8→0, only for a just-created or "→ Today" task). Implemented by screen view-models on top of
/// <c>TaskActions.ConsumeEntrance</c>; task visuals find it on the DataContext of an ancestor.
/// </summary>
public interface ITaskEntranceSource
{
    /// <summary>True once for the task that should animate in; clears the request.</summary>
    bool ConsumeEntrance(long id);
}

/// <summary>
/// The week task card (prototype <c>.card</c>, design-reference lines 534–551): a <see cref="Card"/> (Bg.Card, r8,
/// hover Bg.CardHover, padding/min height from <c>Density.CardPadding</c> / <c>Density.CardMinHeight</c>) holding
/// a 16 px <see cref="TaskCheckBox"/> (toggles), the title (13/500, line height 18, clamped to two lines with an
/// ellipsis, click = edit, always-present strike that fades in when done), and the right-hand meta: priority button
/// (only when set; click = cycle), tag (mono 10.5 Text.Muted) and time (Text.Secondary).
/// <para>DataContext: <see cref="TaskRowViewModel"/>. Completing the task only changes bound properties (check fill,
/// strike and title colour transitions); the card is never rebuilt.</para>
/// <para>Opacity: 0.3 when <see cref="TaskRowViewModel.Dimmed"/> (tag filter); <see cref="Faded"/> = the next-week
/// preview look (0.55, or 0.2 when also dimmed). Changes fade over Motion.Dim.</para>
/// </summary>
public partial class TaskCard : UserControl
{
    public static readonly StyledProperty<bool> FadedProperty =
        AvaloniaProperty.Register<TaskCard, bool>(nameof(Faded));

    public TaskCard() => InitializeComponent();

    /// <summary>Render as a preview card (week view's next-week cell): opacity 0.55 (0.2 if also dimmed).</summary>
    public bool Faded
    {
        get => GetValue(FadedProperty);
        set => SetValue(FadedProperty, value);
    }

    /// <summary>The title text (for measuring and tests).</summary>
    internal StrikeText TitleText => PART_Title;

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
        if (DataContext is TaskRowViewModel row && FindEntranceSource() is { } source && source.ConsumeEntrance(row.Id))
        {
            Animator.SlideFrom(this, 0, M.NewRowY, M.NewRow);
        }
    }

    private ITaskEntranceSource? FindEntranceSource()
    {
        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor is StyledElement { DataContext: ITaskEntranceSource source })
            {
                return source;
            }
        }

        return null;
    }
}
