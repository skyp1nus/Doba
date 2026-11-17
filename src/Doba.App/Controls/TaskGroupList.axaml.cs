using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Controls;

/// <summary>Arguments of <see cref="TaskGroupList.FollowRequestedEvent"/>: the group whose label link was clicked and the
/// clicked element (to compute the zoom origin).</summary>
public sealed class FollowRequestedEventArgs(TaskGroupViewModel group, Control source)
    : RoutedEventArgs(TaskGroupList.FollowRequestedEvent)
{
    public TaskGroupViewModel Group { get; } = group;

    /// <summary>The clicked label button.</summary>
    public Control Origin { get; } = source;
}

/// <summary>
/// A labelled list section (Inbox, Today): header (label in the group's tone colour, or a link button when the group has
/// a <see cref="TaskGroupViewModel.Link"/>; count at the right) and the group's rows as <see cref="TaskListRow"/>s in a
/// <see cref="VirtualizingStackPanel"/>. DataContext is a <see cref="TaskGroupViewModel"/>.
/// </summary>
/// <remarks>
/// A label link raises the bubbling <see cref="FollowRequestedEvent"/> instead of executing
/// <see cref="TaskGroupViewModel.FollowCommand"/>, so the screen can pass the click origin (the clicked element's centre
/// relative to the content area) to <c>IAppCommands.ZoomTo</c>.
/// </remarks>
public partial class TaskGroupList : UserControl
{
    public static readonly RoutedEvent<FollowRequestedEventArgs> FollowRequestedEvent =
        RoutedEvent.Register<TaskGroupList, FollowRequestedEventArgs>(nameof(FollowRequested), RoutingStrategies.Bubble);

    /// <summary>Meta colour for the rows (see <see cref="TaskListRow.MetaForeground"/>). Default Text.Secondary.</summary>
    public static readonly StyledProperty<IBrush?> MetaForegroundProperty =
        TaskListRow.MetaForegroundProperty.AddOwner<TaskGroupList>();

    public TaskGroupList() => InitializeComponent();

    public static IValueConverter IsWarning { get; } = new FuncValueConverter<GroupTone, bool>(t => t == GroupTone.Warning);

    public static IValueConverter IsAccent { get; } = new FuncValueConverter<GroupTone, bool>(t => t == GroupTone.Accent);

    public event EventHandler<FollowRequestedEventArgs>? FollowRequested
    {
        add => AddHandler(FollowRequestedEvent, value);
        remove => RemoveHandler(FollowRequestedEvent, value);
    }

    public IBrush? MetaForeground
    {
        get => GetValue(MetaForegroundProperty);
        set => SetValue(MetaForegroundProperty, value);
    }

    /// <summary>The label link button (tests).</summary>
    internal Button LinkButton => PART_Link;

    private void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskGroupViewModel { HasLink: true } group)
        {
            e.Handled = true;
            RaiseEvent(new FollowRequestedEventArgs(group, PART_Link));
        }
    }
}
