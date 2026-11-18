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

    /// <summary>The page title (h1: title + subtitle), the target of the title entrance every navigation plays (motion spec:
    /// the h1 only; the breadcrumbs above it stay put).</summary>
    public Control TitleBlock => PART_Heading;

    /// <summary>"New task" (focus fallback after an overlay closes).</summary>
    public Button NewTaskButton => PART_NewTask;

    internal Button SidebarToggle => PART_SidebarToggle;

    internal SegmentedSwitch ZoomSwitch => PART_Zoom;

    internal Control CalendarActions => PART_CalendarActions;

    internal Button TodayButton => PART_Today;

    internal Button PreviousButton => PART_Previous;

    internal Button NextButton => PART_Next;

    internal Kbd NewTaskKey => PART_NewTaskKey;

    internal Control Heading => PART_Heading;

    internal TextBlock HeadingTitle => PART_HeadingTitle;

    internal TextBlock HeadingSubtitle => PART_HeadingSubtitle;

    internal ItemsControl CrumbList => PART_Crumbs;
}
