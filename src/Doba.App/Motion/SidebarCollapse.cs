using Avalonia.Controls;

namespace Doba.App.Motion;

/// <summary>
/// The sidebar collapse / expand (motion spec "Згортання сайдбару": 232 ↔ 56 px rail, 340 ms ease-out; the labels fade
/// over <see cref="Motion.RailLabels"/> inside the sidebar itself) without animating layout.
/// </summary>
/// <remarks>
/// <para>The window lays the sidebar column and the main panel's left inset out independently: both are 232 px, or 56 px
/// with the <see cref="RailClass"/> class, and the main panel is drawn over the sidebar. Each toggle changes each width
/// exactly once and slides the main panel with a composition translation (<see cref="Animator"/>), so the eye sees its
/// left edge travel continuously between 232 and 56 while the content is laid out at most once per toggle:</para>
/// <list type="bullet">
/// <item><b>Collapse</b>: the inset becomes 56 at the start (the main panel takes its final, wider layout — the one
/// re-layout), then the panel slides from +176 px to 0 over the still 232 px sidebar, whose labels fade; at the end the
/// column becomes the 56 px rail (hidden under the panel, so nothing moves).</item>
/// <item><b>Expand</b>: the column becomes 232 at the start (under the panel), the panel slides from 0 to +176 px uncovering
/// it while the labels fade in; at the end the inset becomes 232 and the translation returns to 0 in the same frame (the
/// one re-layout, to the final, narrower width).</item>
/// </list>
/// <para>The main panel is never narrower than its final width during the slide, so the window background never shows at
/// its right edge (that part is simply outside the window). A toggle during a slide continues from where the panel is
/// drawn now. Cancelling or completing the slide applies the final geometry at once; with OS animations off it is
/// instant.</para>
/// </remarks>
public sealed class SidebarCollapse
{
    /// <summary>The style class that switches a width from Size.Sidebar to Size.Rail.</summary>
    public const string RailClass = "rail";

    private const string SidebarWidthKey = "Size.Sidebar";
    private const string RailWidthKey = "Size.Rail";

    private readonly Control _column;
    private readonly Control _inset;
    private readonly Control _main;
    private int _generation;

    /// <param name="column">The sidebar column (its width is the sidebar's).</param>
    /// <param name="inset">The element that holds the main panel's left edge (its width is the main panel's x).</param>
    /// <param name="main">The main panel, drawn over the sidebar; only its composition translation is animated.</param>
    public SidebarCollapse(Control column, Control inset, Control main)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(inset);
        ArgumentNullException.ThrowIfNull(main);
        _column = column;
        _inset = inset;
        _main = main;
        IsRail = column.Classes.Contains(RailClass);
    }

    /// <summary>The state the sidebar is in, or is sliding to.</summary>
    public bool IsRail { get; private set; }

    /// <summary>The running slide of the main panel, if any.</summary>
    public MotionHandle? Running => Animator.Running(_main);

    /// <summary>Puts the sidebar in <paramref name="rail"/> state at once (start-up, entering / leaving Settings).</summary>
    public void Snap(bool rail)
    {
        _generation++;
        IsRail = rail;
        Animator.Running(_main)?.Cancel(); // a slide that is cut short shows the panel at rest
        Apply(rail);
    }

    /// <summary>Slides to <paramref name="rail"/> (Collapse button, page-header toggle); no-op if already there.</summary>
    public void Animate(bool rail)
    {
        if (rail == IsRail)
        {
            return;
        }

        if (!Motion.SystemAnimationsEnabled)
        {
            Snap(rail);
            return;
        }

        var sidebar = Width(SidebarWidthKey);
        var railWidth = Width(RailWidthKey);
        var inset = _inset.Classes.Contains(RailClass) ? railWidth : sidebar;
        var drawnLeft = inset + (Animator.Running(_main)?.Current.X ?? 0);

        var generation = ++_generation;
        IsRail = rail;
        if (rail)
        {
            _inset.Classes.Set(RailClass, true);
            _column.Classes.Set(RailClass, false);
            Animator.Animate(_main, MotionState.Offset(drawnLeft - railWidth, 0), MotionState.Rest, Motion.Slide, Motion.EaseOut,
                null, _ => Finish(generation, rail: true));
        }
        else
        {
            _column.Classes.Set(RailClass, false);
            Animator.Animate(_main, MotionState.Offset(drawnLeft - inset, 0), MotionState.Offset(sidebar - inset, 0), Motion.Slide,
                Motion.EaseOut, null, _ => Finish(generation, rail: false));
        }
    }

    private void Finish(int generation, bool rail)
    {
        if (generation != _generation)
        {
            return; // superseded by a newer toggle, which continues from here
        }

        Apply(rail);

        // An expand ends holding the panel +176 px on the render thread; it returns to 0 in the same frame as the inset
        // takes its final width.
        Animator.Reset(_main);
    }

    private void Apply(bool rail)
    {
        _column.Classes.Set(RailClass, rail);
        _inset.Classes.Set(RailClass, rail);
    }

    private double Width(string key) =>
        _column.TryFindResource(key, _column.ActualThemeVariant, out var value) && value is double width
            ? width
            : throw new InvalidOperationException($"Missing size resource {key}.");
}
