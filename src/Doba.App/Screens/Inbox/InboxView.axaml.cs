using Avalonia.Controls;
using Doba.App.Controls;
using Doba.App.Screens.Week;

namespace Doba.App.Screens.Inbox;

/// <summary>The Inbox screen; DataContext is an <see cref="InboxViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class InboxView : UserControl
{
    private readonly ZoomOrigin _origin;

    public InboxView()
    {
        InitializeComponent();
        _origin = new ZoomOrigin(this);
        AddHandler(TaskGroupList.FollowRequestedEvent, OnFollowRequested);
    }

    /// <summary>The page scroller (tests).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>A scheduled-day link: zoom into that day from the clicked link's centre; activated from the keyboard
    /// (Enter / Space) there is no origin, so the transition uses its default 50 % 40 % (DECISIONS "Calendar zooms from
    /// the keyboard").</summary>
    private void OnFollowRequested(object? sender, FollowRequestedEventArgs e)
    {
        if (DataContext is InboxViewModel vm)
        {
            vm.Follow(e.Group, _origin.For(e.Origin));
        }
    }
}
