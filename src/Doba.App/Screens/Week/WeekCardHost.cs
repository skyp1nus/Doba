using Avalonia;
using Avalonia.Controls;
using Doba.App.Controls;
using Doba.App.ViewModels;

namespace Doba.App.Screens.Week;

/// <summary>
/// Hosts the <see cref="TaskCard"/> of one <see cref="WeekCardSlot"/>. When the slot is pointed at another task (a week
/// change reuses the cards by position), the same card shows the new task at once: its data context changes inside a
/// <see cref="SnapScope"/>, so the check fill, title colour, strike and dim do not fade from the previous task's state
/// (the way TaskListRow handles a recycled row). Changes of the same task keep their transitions. A card that holds
/// keyboard focus is not re-pointed: it is replaced by a new one, so focus never lands on another task's card (the shell
/// then moves it to the content area, as for any focused element a navigation removes).
/// </summary>
public sealed class WeekCardHost : Decorator
{
    public static readonly StyledProperty<TaskRowViewModel?> RowProperty =
        AvaloniaProperty.Register<WeekCardHost, TaskRowViewModel?>(nameof(Row));

    private TaskCard _card = new();

    public WeekCardHost()
    {
        // A local value: the card never inherits the slot as its data context.
        _card.DataContext = null;
        Child = _card;
    }

    /// <summary>The task the card shows (bound to <see cref="WeekCardSlot.Row"/>).</summary>
    public TaskRowViewModel? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>The hosted card (tests).</summary>
    internal TaskCard Card => _card;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RowProperty)
        {
            return;
        }

        var row = change.GetNewValue<TaskRowViewModel?>();
        if (_card.IsKeyboardFocusWithin && _card.DataContext is TaskRowViewModel shown && shown.Id != row?.Id)
        {
            _card = new TaskCard { DataContext = row };
            Child = _card;
            return;
        }

        if (_card.IsLoaded && _card.DataContext is not null)
        {
            using (SnapScope.Open(_card))
            {
                _card.DataContext = row;
            }
        }
        else
        {
            _card.DataContext = row;
        }
    }
}
