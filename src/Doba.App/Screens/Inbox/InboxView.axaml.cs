using Avalonia;
using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Screens.Inbox;

/// <summary>The Inbox screen; DataContext is an <see cref="InboxViewModel"/>. Its bounds are the shell's content area.</summary>
public partial class InboxView : UserControl
{
    public InboxView()
    {
        InitializeComponent();
        AddHandler(TaskGroupList.FollowRequestedEvent, OnFollowRequested);
    }

    /// <summary>The page scroller (tests).</summary>
    internal ScrollViewer Scroller => PART_Scroll;

    /// <summary>Centre of <paramref name="element"/> relative to <paramref name="area"/>, as fractions (0..1) of its size.</summary>
    internal static Point? OriginIn(Visual area, Visual element)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(element);
        var size = area.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0
            || element.TranslatePoint(new Point(element.Bounds.Width / 2, element.Bounds.Height / 2), area) is not { } centre)
        {
            return null;
        }

        return new Point(centre.X / size.Width, centre.Y / size.Height);
    }

    private void OnFollowRequested(object? sender, FollowRequestedEventArgs e)
    {
        if (DataContext is InboxViewModel vm)
        {
            vm.Follow(e.Group, OriginIn(this, e.Origin));
        }
    }
}
