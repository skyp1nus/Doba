using Avalonia.Controls;
using Doba.App.Controls;

namespace Doba.App.Shell;

/// <summary>The page header. DataContext: <see cref="PageHeaderViewModel"/>.</summary>
public partial class PageHeader : UserControl
{
    public PageHeader()
    {
        InitializeComponent();
    }

    /// <summary>Breadcrumbs + title: the block content transitions cross-fade with the screen.</summary>
    public Control TitleBlock => PART_Title;

    /// <summary>"New task" (focus fallback after an overlay closes).</summary>
    public Button NewTaskButton => PART_NewTask;

    internal Button SidebarToggle => PART_SidebarToggle;

    internal SegmentedSwitch ZoomSwitch => PART_Zoom;

    internal Control CalendarActions => PART_CalendarActions;

    internal Button TodayButton => PART_Today;

    internal Button PreviousButton => PART_Previous;

    internal Button NextButton => PART_Next;

    internal Control Heading => PART_Heading;

    internal TextBlock HeadingTitle => PART_HeadingTitle;

    internal TextBlock HeadingSubtitle => PART_HeadingSubtitle;

    internal ItemsControl CrumbList => PART_Crumbs;
}
