using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Threading;
using Doba.Core.Model;
using M = Doba.App.Motion.Motion;

namespace Doba.App.Theme;

/// <summary>
/// Applies the live appearance settings to the application resources: accent (Brush.Accent / Accent10 / Accent15 /
/// Accent25 and Color.Accent), density (Density.* keys) and the chrome tint (Brush.Chrome = rgba(22,22,24, op)).
/// </summary>
/// <remarks>
/// Accent and chrome brushes are mutated in place, so both <c>StaticResource</c> and <c>DynamicResource</c> users update;
/// <c>Color.*</c> and <c>Density.*</c> values are written to <see cref="Application.Resources"/> (which wins over the
/// theme's Tokens.axaml), so consume them with <c>DynamicResource</c>.
/// With <c>animate: true</c> colour changes cross-fade over <see cref="M.AccentChange"/> ms (`ease`), as in Settings;
/// when OS animations are off (<see cref="M.SystemAnimationsEnabled"/>) they are instant.
/// </remarks>
public sealed class ThemeService
{
    public const string AccentColorKey = "Color.Accent";
    public const string AccentBrushKey = "Brush.Accent";
    public const string Accent10BrushKey = "Brush.Accent10";
    public const string Accent15BrushKey = "Brush.Accent15";
    public const string Accent25BrushKey = "Brush.Accent25";
    public const string ChromeBrushKey = "Brush.Chrome";
    public const string ChromeBaseColorKey = "Color.Chrome.Base";

    /// <summary>Default chrome transparency (Settings › Window transparency), percent.</summary>
    public const int DefaultChromeOpacity = 72;

    /// <summary>Density keys and their Comfortable / Compact values (the prototype's <c>.compact</c> rules).</summary>
    public static IReadOnlyList<(string Key, object Comfortable, object Compact)> DensityTable { get; } =
    [
        ("Density.RowPadding", new Thickness(4, 11), new Thickness(4, 5)),
        ("Density.TitleSize", 13.5, 13.0),
        ("Density.CardPadding", new Thickness(11, 9), new Thickness(11, 6)),
        ("Density.CardMinHeight", 36.0, 30.0),
        ("Density.CardGap", 9.0, 3.0),
        ("Density.DayCardPadding", new Thickness(12, 11), new Thickness(12, 6)),
        ("Density.DayCardGap", 6.0, 3.0),
    ];

    private static readonly Color s_chromeBaseFallback = Color.FromRgb(22, 22, 24);

    private readonly Application _app;
    private readonly Dictionary<string, ColorTween> _tweens = [];

    public ThemeService(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    public Accent Accent { get; private set; } = Accent.Mist;

    public Density Density { get; private set; } = Density.Comfortable;

    public int ChromeOpacity { get; private set; } = DefaultChromeOpacity;

    /// <summary>Applies accent, density and chrome tint from settings (no animation: use at startup).</summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyAccent(settings.Accent);
        ApplyDensity(settings.Density);
        ApplyChromeOpacity(settings.WindowTransparency);
    }

    public void ApplyAccent(Accent accent, bool animate = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        Accent = accent;
        var color = AccentPalette.ColorOf(accent);
        _app.Resources[AccentColorKey] = color;
        SetBrushColor(AccentBrushKey, color, animate);
        SetBrushColor(Accent10BrushKey, AccentPalette.WithAlpha(color, AccentPalette.Alpha10), animate);
        SetBrushColor(Accent15BrushKey, AccentPalette.WithAlpha(color, AccentPalette.Alpha15), animate);
        SetBrushColor(Accent25BrushKey, AccentPalette.WithAlpha(color, AccentPalette.Alpha25), animate);
    }

    public void ApplyDensity(Density density)
    {
        Dispatcher.UIThread.VerifyAccess();
        Density = density;
        foreach (var (key, comfortable, compact) in DensityTable)
        {
            _app.Resources[key] = density == Density.Compact ? compact : comfortable;
        }
    }

    /// <summary>Sets the chrome tint over the backdrop (sidebar + title bar): rgba(22,22,24, percent / 100).</summary>
    public void ApplyChromeOpacity(int percent, bool animate = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        ChromeOpacity = Math.Clamp(percent, 0, 100);
        var baseColor = _app.TryGetResource(ChromeBaseColorKey, _app.ActualThemeVariant, out var value) && value is Color c
            ? c
            : s_chromeBaseFallback;
        var alpha = (byte)Math.Round(ChromeOpacity / 100.0 * byte.MaxValue);
        SetBrushColor(ChromeBrushKey, AccentPalette.WithAlpha(baseColor, alpha), animate);
    }

    /// <summary>The live brush for a key (mutated in place by this service).</summary>
    public SolidColorBrush GetBrush(string key)
    {
        if (_app.TryGetResource(key, _app.ActualThemeVariant, out var value) && value is SolidColorBrush brush)
        {
            return brush;
        }

        brush = new SolidColorBrush();
        _app.Resources[key] = brush;
        return brush;
    }

    private void SetBrushColor(string key, Color color, bool animate)
    {
        var brush = GetBrush(key);
        if (_tweens.Remove(key, out var running))
        {
            running.Stop();
        }

        var duration = M.Ms(M.AccentChange);
        if (!animate || duration <= TimeSpan.Zero || brush.Color == color)
        {
            brush.Color = color;
            return;
        }

        var tween = new ColorTween(brush, brush.Color, color, duration, () => _tweens.Remove(key));
        _tweens[key] = tween;
        tween.Start();
    }

    /// <summary>The cross-fade running on the brush <paramref name="key"/> (null when none runs); for tests.</summary>
    internal ColorTween? Running(string key) => _tweens.GetValueOrDefault(key);

    /// <summary>A UI-thread colour cross-fade on a resource brush (resource brushes are not in the visual tree, so
    /// Avalonia transitions do not run on them).</summary>
    internal sealed class ColorTween(SolidColorBrush brush, Color from, Color to, TimeSpan duration, Action onDone)
    {
        private static readonly TimeSpan s_frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly long _start = Stopwatch.GetTimestamp();
        private DispatcherTimer? _timer;

        public Color From => from;

        public Color To => to;

        public TimeSpan Duration => duration;

        /// <summary>Motion.EaseStd (`ease`), as the prototype's <c>transition: background-color 0.3s ease</c>.</summary>
        public Easing Easing { get; } = M.EaseStd;

        /// <summary>The colour shown at <paramref name="progress"/> (0..1 of <see cref="Duration"/>).</summary>
        public Color ColorAt(double progress)
        {
            var e = Easing.Ease(Math.Clamp(progress, 0, 1));
            return Color.FromArgb(Lerp(from.A, to.A, e), Lerp(from.R, to.R, e), Lerp(from.G, to.G, e), Lerp(from.B, to.B, e));
        }

        public void Start()
        {
            _timer = new DispatcherTimer(s_frame, DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        private void Tick()
        {
            var t = Math.Clamp(Stopwatch.GetElapsedTime(_start) / duration, 0, 1);
            brush.Color = ColorAt(t);
            if (t >= 1)
            {
                Stop();
                onDone();
            }
        }

        private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
    }
}
