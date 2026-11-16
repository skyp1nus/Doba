using Avalonia;
using Avalonia.Animation.Easings;

namespace Doba.App.Motion;

/// <summary>
/// The single source of every duration and easing in the app (docs/design/motion-spec.md, "Токени").
/// Views and code must not contain literal durations or curves; they read these members.
/// When the OS "Animation effects" setting is off, <see cref="Ms"/> returns zero and everything is instant.
/// </summary>
public static class Motion
{
    // cubic-bezier(0.22, 1, 0.36, 1) — every entrance, slide, indicator and toggle.
    public static readonly Point OutP1 = new(0.22, 1), OutP2 = new(0.36, 1);

    // ease-in = cubic-bezier(0.42, 0, 1, 1) — screen exit, dialog close.
    public static readonly Point InP1 = new(0.42, 0), InP2 = new(1, 1);

    // ease = cubic-bezier(0.25, 0.1, 0.25, 1) — hover, colour/background changes.
    public static readonly Point StdP1 = new(0.25, 0.1), StdP2 = new(0.25, 1);

    public const int Hover = 180;
    public const int HoverFast = 150;

    /// <summary>200 ms `ease`: sidebar/crumb/link text colour, day-header opacity, bar brightness (prototype `0.2s ease`).</summary>
    public const int ColorChange = 200;
    public const int Exit = 130;
    public const int DialogExit = 150;
    public const int Pop = 180;
    public const int Check = 250;
    public const int CheckMark = 200;
    public const int Strike = 300;
    public const int Dim = 300;
    public const int Shade = 220;
    public const int DialogIn = 320;
    public const int Slide = 340;
    public const int SidebarFade = 220;
    public const int RailLabels = 180;
    public const int Title = 360;
    public const int NewRow = 360;
    public const int Section = 380;
    public const int Indicator = 380;
    public const int SegIndicator = 420;
    public const int SegColor = 250;
    public const int Toggle = 250;
    public const int ToggleTrack = 200;
    public const int Segment = 200;
    public const int WheelText = 200;
    public const int WheelSettle = 120;
    public const int AccentChange = 300;
    public const int Zoom = 460;
    public const int Progress = 500;
    public const int Launch = 520;

    /// <summary>Distances (px) and scales used by the transitions.</summary>
    public const double ZoomInFrom = 0.86, ZoomOutFrom = 1.10, ZoomBlur = 6,
        SlideX = 32, SectionEnterY = 18, SectionExitY = 10, TitleY = 6, LaunchY = 12, NewRowY = -8,
        SettingsSidebarX = 16, DialogY = 10, DialogScale = 0.97, DialogExitY = 6, DialogExitScale = 0.98,
        PopY = -4, PopScale = 0.98, NavStep = 33, SegStep = 64, ToggleTravel = 16;

    /// <summary>False when the OS asks for no animations (Windows: Settings › Accessibility › Visual effects › Animation effects).</summary>
    public static bool SystemAnimationsEnabled { get; set; } = true;

    public static TimeSpan Ms(int ms) => SystemAnimationsEnabled ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

    public static Easing EaseOut { get; } = new SplineEasing(OutP1.X, OutP1.Y, OutP2.X, OutP2.Y);

    public static Easing EaseIn { get; } = new SplineEasing(InP1.X, InP1.Y, InP2.X, InP2.Y);

    public static Easing EaseStd { get; } = new SplineEasing(StdP1.X, StdP1.Y, StdP2.X, StdP2.Y);
}
