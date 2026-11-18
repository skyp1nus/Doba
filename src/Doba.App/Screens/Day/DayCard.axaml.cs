using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Doba.App.ViewModels;
using Doba.Core.Model;

namespace Doba.App.Screens.Day;

/// <summary>
/// A single-day task on the Day screen (prototype <c>.card</c> in the Day list): checkbox (toggles), wrapping title
/// (click = edit, strike fades in when done) and, when the task has a priority, tag or time, the meta row — priority
/// button with icon and name (click = cycle), <c>#tag</c>, time. DataContext: <see cref="ViewModels.TaskRowViewModel"/>;
/// completing the task only changes bound properties, the card is never rebuilt. Dimmed (tag filter) fades to
/// <c>Opacity.Dimmed</c>. Plays the new-row entrance once for the task the dialog just created.
/// Keyboard: Space on any focused part of the card (checkbox, title, priority) completes the task, as on list rows and
/// week cards; Enter keeps the focused button's own action.
/// </summary>
public partial class DayCard : UserControl
{
    public DayCard()
    {
        InitializeComponent();
        // Tunnel: the title / priority buttons would otherwise click on Space (open the dialog, cycle the priority).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>"High", "ASAP"… (empty for no priority; the button is hidden then).</summary>
    public static IValueConverter PriorityName { get; } = new FuncValueConverter<Priority, string>(p => p.DisplayName());

    /// <summary>The title text (tests).</summary>
    internal TextBlock TitleText => PART_Title;

    /// <summary>The title button (tests).</summary>
    internal Button TitleButton => PART_TitleButton;

    /// <summary>The meta row (tests).</summary>
    internal Control MetaRow => PART_Meta;

    /// <summary>The priority button (tests).</summary>
    internal Button PriorityButton => PART_Priority;

    /// <summary>The priority name next to the icon (tests).</summary>
    internal TextBlock PriorityText => PART_PriorityName;

    /// <summary>The card surface (opacity / dimmed class; tests).</summary>
    internal Control Surface => PART_Card;

    /// <summary>Space completes the task (key repeat is swallowed by the window's shortcuts, so a held Space acts once).</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && DataContext is TaskRowViewModel row
            && row.ToggleCommand.CanExecute(null))
        {
            row.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DayEntrance.TryPlay(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
        {
            DayEntrance.TryPlay(this);
        }
    }
}
