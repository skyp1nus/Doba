using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The Today/Day schedule: header, hour grid 07–23 (48 px/hour), blocks and the now-line. DataContext is a
/// <see cref="ScheduleViewModel"/>; clicking a block toggles the task (prototype behaviour). Width is the caller's
/// (design: <c>Size.Schedule.Width</c> = 380 inside a 24 px padded aside with a Border.Subtle left border).
/// </summary>
public partial class ScheduleTimeline : UserControl
{
    public ScheduleTimeline() => InitializeComponent();

    /// <summary>The now-line row (tests).</summary>
    internal Control NowLine => PART_Now;

    /// <summary>The blocks list (tests).</summary>
    internal ItemsControl BlockList => PART_Blocks;
}
