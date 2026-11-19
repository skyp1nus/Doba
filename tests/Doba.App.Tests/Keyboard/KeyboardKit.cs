using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Tests.DesignSystem;
using Doba.App.Tests.Support;

namespace Doba.App.Tests.Keyboard;

/// <summary>A clock the tests move by hand (the G chord's timeout).</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks = TimeSpan.TicksPerDay;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}

/// <summary>Real headless key events, Tab walks and focus-visual checks for the keyboard tests.</summary>
internal static class KeyboardKit
{
    /// <summary>Presses (without releasing) <paramref name="key"/>: a second call before <see cref="Release"/> is an
    /// auto-repeat, exactly what Windows sends for a held key.</summary>
    public static void Down(ShellFixture shell, Key key, RawInputModifiers mods = RawInputModifiers.None,
        PhysicalKey physical = PhysicalKey.None, string? symbol = null)
    {
        shell.Window.KeyPress(key, mods, physical, symbol);
        shell.Settle();
    }

    public static void Release(ShellFixture shell, Key key, RawInputModifiers mods = RawInputModifiers.None,
        PhysicalKey physical = PhysicalKey.None, string? symbol = null)
    {
        shell.Window.KeyRelease(key, mods, physical, symbol);
        shell.Settle();
    }

    /// <summary>A full key press with its physical key and the character it types (as a real keyboard sends it).</summary>
    public static void Type(ShellFixture shell, Key key, PhysicalKey physical, string symbol)
    {
        shell.Window.KeyPress(key, RawInputModifiers.None, physical, symbol);
        shell.Window.KeyTextInput(symbol);
        shell.Window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        shell.Settle();
    }

    public static IInputElement? Focused(ShellFixture shell) => shell.Window.FocusManager?.GetFocusedElement();

    /// <summary>Focuses <paramref name="start"/> from the keyboard and presses Tab (Shift+Tab when
    /// <paramref name="backwards"/>) until focus is back on it. Returns the controls in visiting order, start first.</summary>
    public static List<Control> Walk(ShellFixture shell, Control start, bool backwards = false, Action<Control>? each = null,
        int max = 400)
    {
        if (start.IsFocused)
        {
            shell.Window.ScreenHost.Focus(); // e.g. a clicked pill: re-focus it from the keyboard
            shell.Settle();
        }

        Assert.True(start.Focus(NavigationMethod.Tab), "The walk's start does not take focus.");
        shell.Settle();
        each?.Invoke(start);
        var visited = new List<Control> { start };
        for (var i = 0; i < max; i++)
        {
            shell.Key(Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
            var focused = Assert.IsAssignableFrom<Control>(Focused(shell));
            if (ReferenceEquals(focused, start))
            {
                return visited;
            }

            each?.Invoke(focused);
            visited.Add(focused);
        }

        Assert.Fail($"Tab did not come back to {Describe(start)} after {max} presses.");
        return visited;
    }

    /// <summary>
    /// What Tab must reach inside <paramref name="scope"/>: every focusable, enabled, visible tab stop that holds no
    /// other one (a container such as the overlay panel or a list box is not a stop itself). A tab-once group (a list
    /// box: Tab enters its selected item, arrows move inside) counts once, keyed by the group.
    /// </summary>
    public static HashSet<Visual> Stops(Visual scope)
    {
        var candidates = new[] { scope }.Concat(scope.GetVisualDescendants())
            .OfType<InputElement>()
            .Where(IsStop)
            .ToList();
        return candidates
            .Where(c => !candidates.Any(o => !ReferenceEquals(o, c) && c.IsVisualAncestorOf(o)))
            .Select(c => GroupKey(c))
            .ToHashSet();
    }

    /// <summary>The controls Tab lands on in <paramref name="scope"/>: each stop, a list box by its selected item.</summary>
    public static IEnumerable<Control> FocusTargets(Visual scope) =>
        Stops(scope).Select(s => s is SelectingItemsControl list
            ? list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) ?? throw new InvalidOperationException(Describe(list) + " has no items.")
            : (Control)s);

    /// <summary>The control itself, or the tab-once group (list box) it belongs to.</summary>
    public static Visual GroupKey(Visual control)
    {
        for (var v = control.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            if (v is InputElement ie && KeyboardNavigation.GetTabNavigation(ie) == KeyboardNavigationMode.Once)
            {
                return v;
            }
        }

        return control;
    }

    private static bool IsStop(InputElement e) =>
        e.Focusable && e.IsEffectivelyVisible && e.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(e);

    /// <summary>Opacity of the control as drawn (its own times every ancestor's).</summary>
    public static double EffectiveOpacity(Visual v)
    {
        var o = 1.0;
        for (Visual? x = v; x is not null; x = x.GetVisualParent())
        {
            o *= x.Opacity;
        }

        return o;
    }

    /// <summary>Asserts the walk visits each stop of <paramref name="scope"/> exactly once and nothing else.</summary>
    public static void AssertVisitsEveryStopOnce(IReadOnlyList<Control> walk, Visual scope) =>
        AssertVisitsEveryStopOnce(walk, Stops(scope));

    /// <summary>Asserts the walk visits each of <paramref name="expected"/> (see <see cref="Stops"/>) exactly once.</summary>
    public static void AssertVisitsEveryStopOnce(IReadOnlyList<Control> walk, HashSet<Visual> expected)
    {
        var keys = walk.Select(GroupKey).ToList();
        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => Describe(g.Key)).ToList();
        Assert.True(duplicates.Count == 0, "Visited more than once: " + string.Join(", ", duplicates));
        var missing = expected.Where(e => !keys.Contains(e)).Select(Describe).ToList();
        var extra = keys.Where(k => !expected.Contains(k)).Select(Describe).ToList();
        Assert.True(missing.Count == 0, "Never reached by Tab: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0, "Reached but not an expected stop: " + string.Join(", ", extra));
    }

    /// <summary>The focused control is visible, enabled and not transparent (checked as it gets focus).</summary>
    public static void AssertShowable(Control c) =>
        Assert.True(c.IsEffectivelyVisible && c.IsEffectivelyEnabled && EffectiveOpacity(c) > 0.01,
            $"{Describe(c)} took focus while invisible or disabled.");

    /// <summary>Shift+Tab from the same start visits the same controls in reverse.</summary>
    public static void AssertReverses(IReadOnlyList<Control> forward, IReadOnlyList<Control> backward)
    {
        var expected = new List<Control> { forward[0] };
        expected.AddRange(forward.Skip(1).Reverse());
        Assert.Equal(expected.Select(Describe), backward.Select(Describe));
        Assert.True(expected.SequenceEqual(backward));
    }

    /// <summary>The window-coordinate bounds of <paramref name="c"/>.</summary>
    public static Rect Bounds(Control c, Visual relativeTo)
    {
        var p = c.TranslatePoint(default, relativeTo) ?? throw new InvalidOperationException(Describe(c) + " is not in the window.");
        return new Rect(p, c.Bounds.Size);
    }

    /// <summary>
    /// Reading order inside each region: within the region's part of the (cyclic) walk, every next control lies to the
    /// right of the previous one or below it. Exactly one step may break it: the wrap from the last control back to the
    /// first. <paramref name="regionOf"/> maps a control to its region (title bar, sidebar, header, content, dialog,
    /// popover…).
    /// </summary>
    public static void AssertReadingOrder(IReadOnlyList<Control> walk, Visual window, Func<Control, Visual> regionOf) =>
        AssertReadingOrder(walk, c => Bounds(c, window), regionOf);

    /// <summary><see cref="AssertReadingOrder(IReadOnlyList{Control}, Visual, Func{Control, Visual})"/> on bounds recorded
    /// earlier (controls that have left the tree since).</summary>
    public static void AssertReadingOrder(IReadOnlyList<Control> walk, Func<Control, Rect> boundsOf, Func<Control, Visual> regionOf)
    {
        foreach (var region in walk.GroupBy(regionOf))
        {
            var items = region.ToList();
            if (items.Count < 2)
            {
                continue;
            }

            var breaks = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var a = items[i];
                var b = items[(i + 1) % items.Count];
                if (!Follows(boundsOf(a), boundsOf(b)))
                {
                    breaks.Add($"{Describe(a)} → {Describe(b)}");
                }
            }

            Assert.True(breaks.Count <= 1,
                $"Tab order in {Describe(region.Key)} is not the reading order:\n" + string.Join('\n', breaks));
        }
    }

    /// <summary>b comes after a when it is below a or to its right (1 px tolerance for shared edges).</summary>
    private static bool Follows(Rect a, Rect b) => b.Top >= a.Bottom - 1 || b.Left >= a.Right - 1;

    /// <summary>The nearest ancestor (or the control itself) that is one of <paramref name="regions"/>.</summary>
    public static Visual RegionOf(Control c, IReadOnlyCollection<Visual> regions)
    {
        for (Visual? v = c; v is not null; v = v.GetVisualParent())
        {
            if (regions.Contains(v))
            {
                return v;
            }
        }

        throw new InvalidOperationException(Describe(c) + " is in no region.");
    }

    /// <summary>
    /// The control shows the keyboard focus state: <c>:focus-visible</c> and the app's focus ring adorner on it; text
    /// boxes have no ring by design (prototype <c>.qa:focus{outline:none}</c>) — the caret is their cue.
    /// </summary>
    public static void AssertFocusVisible(Control c)
    {
        Assert.True(c.IsFocused, Describe(c) + " is not focused.");
        if (c is TextBox box)
        {
            Assert.Null(box.FocusAdorner);
            return;
        }

        Assert.True(c.Classes.Contains(":focus-visible"), Describe(c) + " has no :focus-visible.");
        Assert.True(HasFocusRing(c), Describe(c) + " shows no focus ring.");
    }

    /// <summary>The adorner layer holds a visible <see cref="FocusRing"/> for <paramref name="c"/>.</summary>
    public static bool HasFocusRing(Control c) =>
        AdornerLayer.GetAdornerLayer(c) is { } layer
        && layer.Children.Any(a => a is FocusRing { IsEffectivelyVisible: true } && ReferenceEquals(AdornerLayer.GetAdornedElement(a), c));

    /// <summary>The visible <see cref="FocusRing"/> adorning <paramref name="c"/>, if any.</summary>
    public static FocusRing? RingOf(Control c) =>
        AdornerLayer.GetAdornerLayer(c)?.Children.OfType<FocusRing>()
            .FirstOrDefault(a => a.IsEffectivelyVisible && ReferenceEquals(AdornerLayer.GetAdornedElement(a), c));

    /// <summary>
    /// Pixel check: with <paramref name="c"/> focused from the keyboard its ring is drawn on at least three of its four
    /// edges, compared with a frame where <paramref name="neutral"/> has focus. The ring's colour, width and offset are
    /// read from the control's <see cref="FocusRing"/> adorner: the app's default is 1 px Brush.Border.Focus with CSS
    /// <c>outline-offset: -2px</c> (the line lies 1 px inside the edge); a schedule block's is 1.5 px Schedule.Focus
    /// 2 px outside it.
    /// </summary>
    public static void AssertRingDrawn(ShellFixture shell, Control c, Control neutral)
    {
        Assert.True(c.Focus(NavigationMethod.Tab));                    // first: scroll it into view if needed
        shell.Settle();
        Assert.True(neutral.Focus(NavigationMethod.Tab));
        shell.Settle();
        using var before = Frame(shell);
        Assert.True(c.Focus(NavigationMethod.Tab));
        shell.Settle();
        using var after = Frame(shell);

        var adorner = RingOf(c) ?? throw new InvalidOperationException(Describe(c) + " has no focus ring.");
        var stroke = adorner.Stroke is ISolidColorBrush brush ? brush.Color : throw new InvalidOperationException("Ring stroke is not a solid colour.");
        var ring = Color.FromRgb(stroke.R, stroke.G, stroke.B);
        var line = adorner.Offset + (adorner.StrokeThickness / 2); // the stroke's centre line, outward from the edge
        var r = Bounds(c, shell.Window);
        var midX = (int)Math.Round(r.Center.X);
        var midY = (int)Math.Round(r.Center.Y);
        (int X, int Y, bool Vertical)[] edges =
        [
            ((int)Math.Floor(r.Left - line), midY, true),
            ((int)Math.Floor(r.Right + line), midY, true),
            (midX, (int)Math.Floor(r.Top - line), false),
            (midX, (int)Math.Floor(r.Bottom + line), false),
        ];

        var drawn = 0;
        foreach (var (x, y, vertical) in edges)
        {
            // The 1 px line can straddle two pixels when the control sits on a fractional position.
            var hit = false;
            for (var d = 0; d <= 1 && !hit; d++)
            {
                var (px, py) = vertical ? (x + d, y) : (x, y + d);
                var a = Pixels.At(before, px, py);
                var b = Pixels.At(after, px, py);
                hit = Distance(b, ring) + 10 <= Distance(a, ring);
            }

            drawn += hit ? 1 : 0;
        }

        Assert.True(drawn >= 3, $"{Describe(c)} ({c.Theme?.TargetType?.Name ?? c.GetType().Name}): focus ring drawn on {drawn}/4 edges.");
    }

    private static WriteableBitmap Frame(ShellFixture shell)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return shell.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
    }

    private static int Distance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>One representative per control look (theme + type) among <paramref name="walk"/>; text boxes excluded.</summary>
    public static IEnumerable<Control> OnePerTheme(IEnumerable<Control> walk) =>
        walk.Where(c => c is not TextBox)
            .GroupBy(c => (c.Theme, c.GetType()))
            .Select(g => g.First());

    public static string Describe(Visual? v)
    {
        if (v is not Control c)
        {
            return v?.GetType().Name ?? "null";
        }

        var name = Avalonia.Automation.AutomationProperties.GetName(c);
        if (string.IsNullOrEmpty(name))
        {
            name = c.Name ?? (c is ContentControl { Content: string s } ? s : null);
        }

        return $"{c.GetType().Name} '{name}'";
    }
}
