namespace Doba.App.Motion;

/// <summary>
/// The <see cref="Motion"/> duration tokens as <see cref="TimeSpan"/>s for XAML, already passed through
/// <see cref="Motion.Ms"/> (so they are zero when the OS asks for no animations).
/// </summary>
/// <remarks>
/// Use them with <c>x:Static</c> wherever a duration is needed in XAML, e.g.
/// <c>&lt;BrushTransition Property="Background" Duration="{x:Static m:MotionTimes.Hover}" Easing="{x:Static m:Motion.EaseStd}" /&gt;</c>.
/// <para>
/// Why x:Static and not a resource: <c>Transitions</c> are not resource hosts, so <c>DynamicResource</c> does not resolve
/// inside them, and <c>StaticResource</c> lookups across included files are resolved once at load. An <c>x:Static</c>
/// inside a <c>ControlTemplate</c> is evaluated every time the template is applied, so controls created after
/// <see cref="Motion.SystemAnimationsEnabled"/> changes pick up the new value. Transitions of controls that already exist
/// are re-timed by <see cref="MotionSync"/> in tracked windows (zeroed when animations go off, restored when they come
/// back; one created while they were off stays instant until it is re-templated).
/// </para>
/// </remarks>
public static class MotionTimes
{
    public static TimeSpan Hover => Motion.Ms(Motion.Hover);
    public static TimeSpan HoverFast => Motion.Ms(Motion.HoverFast);
    public static TimeSpan Exit => Motion.Ms(Motion.Exit);
    public static TimeSpan DialogExit => Motion.Ms(Motion.DialogExit);
    public static TimeSpan Pop => Motion.Ms(Motion.Pop);
    public static TimeSpan Check => Motion.Ms(Motion.Check);
    public static TimeSpan CheckMark => Motion.Ms(Motion.CheckMark);
    public static TimeSpan Strike => Motion.Ms(Motion.Strike);
    public static TimeSpan Dim => Motion.Ms(Motion.Dim);
    public static TimeSpan Shade => Motion.Ms(Motion.Shade);
    public static TimeSpan DialogIn => Motion.Ms(Motion.DialogIn);
    public static TimeSpan Slide => Motion.Ms(Motion.Slide);
    public static TimeSpan SidebarFade => Motion.Ms(Motion.SidebarFade);
    public static TimeSpan RailLabels => Motion.Ms(Motion.RailLabels);
    public static TimeSpan Title => Motion.Ms(Motion.Title);
    public static TimeSpan NewRow => Motion.Ms(Motion.NewRow);
    public static TimeSpan Section => Motion.Ms(Motion.Section);
    public static TimeSpan Indicator => Motion.Ms(Motion.Indicator);
    public static TimeSpan SegIndicator => Motion.Ms(Motion.SegIndicator);
    public static TimeSpan SegColor => Motion.Ms(Motion.SegColor);
    public static TimeSpan Toggle => Motion.Ms(Motion.Toggle);
    public static TimeSpan ToggleTrack => Motion.Ms(Motion.ToggleTrack);
    public static TimeSpan Segment => Motion.Ms(Motion.Segment);
    public static TimeSpan WheelText => Motion.Ms(Motion.WheelText);
    public static TimeSpan WheelSettle => Motion.Ms(Motion.WheelSettle);
    public static TimeSpan AccentChange => Motion.Ms(Motion.AccentChange);
    public static TimeSpan Zoom => Motion.Ms(Motion.Zoom);
    public static TimeSpan Progress => Motion.Ms(Motion.Progress);
    public static TimeSpan Launch => Motion.Ms(Motion.Launch);
    public static TimeSpan Snap => Motion.Ms(Motion.Snap);
    public static TimeSpan Lift => Motion.Ms(Motion.Lift);
    public static TimeSpan Land => Motion.Ms(Motion.Land);
    public static TimeSpan Fade => Motion.Ms(Motion.Fade);

    /// <summary>The time chip's hold after an arrow-key step: not an animation, so never zeroed (see <see cref="Motion.ChipHold"/>).</summary>
    public static TimeSpan ChipHold => TimeSpan.FromMilliseconds(Motion.ChipHold);

    /// <summary>The "hover/press" family of the prototype: 200 ms `ease` on nav colours, crumbs, header opacity.</summary>
    public static TimeSpan Color => Motion.Ms(Motion.ColorChange);
}
