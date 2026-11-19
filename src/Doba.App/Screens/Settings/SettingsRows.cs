using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.App.Theme;
using Doba.Core.Model;

namespace Doba.App.Screens.Settings;

/// <summary>
/// One settings row (the prototype's <c>cfg.sections[].rows[]</c>): a label (14 Medium), a description (12.5 muted,
/// optionally mono) and a control on the right chosen by the row's type. Rows read <see cref="AppSettings"/> in
/// <see cref="Sync"/> and write through <see cref="ISettingsService.Update"/>; a sync never writes back.
/// </summary>
public abstract partial class SettingsRow : ObservableObject
{
    protected SettingsRow(string key, string label, string description)
    {
        Key = key;
        Label = label;
        Description = description;
    }

    /// <summary>The prototype's row key ("accent", "opacity", "theme" …).</summary>
    public string Key { get; }

    public string Label { get; }

    public string Description { get; }

    /// <summary>The description is a path (Data › Stored on this PC): drawn in Geist Mono.</summary>
    public bool MonoDescription { get; init; }

    /// <summary>Inline feedback under the description (export/import result or error); empty = hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>True when <see cref="Message"/> is an error (drawn in Danger instead of Text.Muted).</summary>
    [ObservableProperty] public partial bool IsError { get; set; }

    public bool HasMessage => Message.Length > 0;

    /// <summary>False dims the label and description and disables the control (Data › Claude can and Connect Claude
    /// while Claude access is off; every Claude row in demo mode).</summary>
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;

    /// <summary>True while <see cref="Sync"/> pushes settings into the row (property changes must not write back).</summary>
    protected bool Syncing { get; private set; }

    /// <summary>Reflects <paramref name="settings"/> without calling <see cref="ISettingsService.Update"/>.</summary>
    public void Sync(AppSettings settings)
    {
        Syncing = true;
        try
        {
            OnSync(settings);
        }
        finally
        {
            Syncing = false;
        }
    }

    protected virtual void OnSync(AppSettings settings)
    {
    }
}

/// <summary>On/off switch (Carry over, Completion sound, Launch at startup, Keep running in the tray, Daily backup).</summary>
public sealed partial class SwitchRow : SettingsRow
{
    private readonly ISettingsService _settings;
    private readonly Func<AppSettings, bool> _get;
    private readonly Func<AppSettings, bool, AppSettings> _set;

    public SwitchRow(string key, string label, string description, ISettingsService settings,
        Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set)
        : base(key, label, description)
    {
        _settings = settings;
        _get = get;
        _set = set;
        Sync(settings.Current);
    }

    [ObservableProperty] public partial bool IsOn { get; set; }

    protected override void OnSync(AppSettings settings) => IsOn = _get(settings);

    partial void OnIsOnChanged(bool value)
    {
        if (Syncing)
        {
            return;
        }

        _settings.Update(s => _set(s, value));
    }
}

/// <summary>Settings-style segmented control ("Dark | System", "Monday | Sunday", "None | Low | Med | High").</summary>
public sealed partial class SegmentRow : SettingsRow
{
    private readonly ISettingsService _settings;
    private readonly Func<AppSettings, int> _get;
    private readonly Func<AppSettings, int, AppSettings> _set;

    private SegmentRow(string key, string label, string description, ISettingsService settings, IReadOnlyList<string> options,
        Func<AppSettings, int> get, Func<AppSettings, int, AppSettings> set)
        : base(key, label, description)
    {
        _settings = settings;
        Options = options;
        _get = get;
        _set = set;
        Sync(settings.Current);
    }

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] public partial int SelectedIndex { get; set; }

    /// <summary>A row over the values of <typeparamref name="T"/>, in the order given.</summary>
    public static SegmentRow Create<T>(string key, string label, string description, ISettingsService settings,
        IReadOnlyList<(T Value, string Label)> options, Func<AppSettings, T> get, Func<AppSettings, T, AppSettings> set)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);
        int IndexOf(AppSettings s)
        {
            var current = get(s);
            for (int i = 0; i < options.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(options[i].Value, current))
                {
                    return i;
                }
            }

            return -1;
        }

        return new SegmentRow(key, label, description, settings, [.. options.Select(o => o.Label)],
            IndexOf, (s, i) => set(s, options[i].Value));
    }

    /// <summary>Selects the option at <paramref name="index"/> (what a click on a segment does).</summary>
    public void Pick(int index) => SelectedIndex = index;

    protected override void OnSync(AppSettings settings) => SelectedIndex = _get(settings);

    partial void OnSelectedIndexChanged(int value)
    {
        if (Syncing || value < 0 || value >= Options.Count)
        {
            return;
        }

        _settings.Update(s => _set(s, value));
    }
}

/// <summary>One accent swatch: a 22 px circle with the selection ring.</summary>
public sealed partial class SwatchOption : ObservableObject
{
    private readonly Action<Accent> _pick;

    public SwatchOption(Accent accent, Action<Accent> pick)
    {
        Accent = accent;
        _pick = pick;
        Fill = new ImmutableSolidColorBrush(AccentPalette.ColorOf(accent));
    }

    public Accent Accent { get; }

    /// <summary>"Mist", "Sage", "Sand", "Gray" (tooltip / accessible name).</summary>
    public string Name => AccentPalette.NameOf(Accent);

    public IBrush Fill { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Pick() => _pick(Accent);
}

/// <summary>Appearance › Accent color.</summary>
public sealed class SwatchRow : SettingsRow
{
    private static readonly Accent[] s_accents = [Accent.Mist, Accent.Sage, Accent.Sand, Accent.Gray];

    public SwatchRow(string key, string label, string description, ISettingsService settings)
        : base(key, label, description)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Swatches = [.. s_accents.Select(a => new SwatchOption(a, picked => settings.Update(s => s with { Accent = picked })))];
        Sync(settings.Current);
    }

    public IReadOnlyList<SwatchOption> Swatches { get; }

    protected override void OnSync(AppSettings settings)
    {
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = swatch.Accent == settings.Accent;
        }
    }
}

/// <summary>Appearance › Window transparency: a 0–100 slider and the "72%" label.</summary>
public sealed partial class RangeRow : SettingsRow
{
    private readonly ISettingsService _settings;

    public RangeRow(string key, string label, string description, ISettingsService settings)
        : base(key, label, description)
    {
        _settings = settings;
        Sync(settings.Current);
    }

    public static double Minimum => 0;

    public static double Maximum => 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueLabel))]
    public partial double Value { get; set; }

    public string ValueLabel => FormattableString.Invariant($"{(int)Math.Round(Value)}%");

    protected override void OnSync(AppSettings settings) => Value = settings.WindowTransparency;

    partial void OnValueChanged(double value)
    {
        if (Syncing)
        {
            return;
        }

        int percent = (int)Math.Round(Math.Clamp(value, Minimum, Maximum));
        _settings.Update(s => s with { WindowTransparency = percent });
    }
}

/// <summary>
/// One row of a density card's mini preview, in whole pixels: <see cref="Margin"/> (top) and <see cref="Height"/> inside
/// the 76 px preview, the 8 px check box and 4 px bar offsets inside the row, and the bar width.
/// </summary>
public sealed record DensityPreviewRow(Thickness Margin, double Height, Thickness CheckMargin, Thickness BarMargin, double BarWidth);

/// <summary>One density option card (Comfortable / Compact) with its mini preview.</summary>
public sealed partial class DensityOption : ObservableObject
{
    /// <summary>Preview height (prototype: 76 px), row inner width (150 − 2·10 padding − 2·6 row padding) and the bar's
    /// left edge in a row (padding 6 + box 8 + gap 6).</summary>
    private const double PreviewHeight = 76, RowInnerWidth = 118, BarLeft = 20, CheckSize = 8, BarHeight = 4;

    /// <summary>Bar widths of the four preview rows, as fractions of the row's inner width (prototype: 70/52/64/46 %).</summary>
    private static readonly double[] s_barShares = [0.70, 0.52, 0.64, 0.46];

    private readonly Action<Density> _pick;

    public DensityOption(Density density, string label, double rowHeight, double rowGap, Action<Density> pick)
    {
        Density = density;
        Label = label;
        RowHeight = rowHeight;
        RowGap = rowGap;
        _pick = pick;
        PreviewRows = LayOutPreview(rowHeight, rowGap);
    }

    public Density Density { get; }

    public string Label { get; }

    /// <summary>Height of each preview row: 14.5 (Comfortable, see <see cref="DensityRow.ComfortableRowHeight"/>) or 12 (Compact).</summary>
    public double RowHeight { get; }

    /// <summary>Gap between preview rows: 6 (Comfortable) or 3 (Compact).</summary>
    public double RowGap { get; }

    /// <summary>The four preview rows, snapped to whole pixels.</summary>
    public IReadOnlyList<DensityPreviewRow> PreviewRows { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>
    /// The prototype's flex column (rows <paramref name="rowHeight"/> tall, <paramref name="gap"/> apart, shrunk to fit
    /// and centred in 76 px; box and bar centred in each row), with every edge snapped to a whole pixel the way the
    /// reference render paints it (an edge on a half pixel snaps to the pixel above). Laid out fractionally, the 14.5 px rows and the 9.5 px centring
    /// offset would be drawn with blurred edges.
    /// </summary>
    private static DensityPreviewRow[] LayOutPreview(double rowHeight, double gap)
    {
        var count = s_barShares.Length;
        var height = Math.Min(rowHeight, (PreviewHeight - ((count - 1) * gap)) / count);
        var offset = (PreviewHeight - ((count * height) + ((count - 1) * gap))) / 2;
        var rows = new DensityPreviewRow[count];
        for (var i = 0; i < count; i++)
        {
            var top = offset + (i * (height + gap));
            var snappedTop = Snap(top);
            rows[i] = new DensityPreviewRow(
                new Thickness(0, snappedTop, 0, 0),
                Snap(top + height) - snappedTop,
                new Thickness(6, Snap(top + ((height - CheckSize) / 2)) - snappedTop, 0, 0),
                new Thickness(BarLeft, Snap(top + ((height - BarHeight) / 2)) - snappedTop, 0, 0),
                Math.Round(s_barShares[i] * RowInnerWidth));
        }

        return rows;

        static double Snap(double v) => Math.Ceiling(v - 0.5);
    }

    [RelayCommand]
    private void Pick() => _pick(Density);
}

/// <summary>Appearance › Density.</summary>
public sealed class DensityRow : SettingsRow
{
    public DensityRow(string key, string label, string description, ISettingsService settings)
        : base(key, label, description)
    {
        ArgumentNullException.ThrowIfNull(settings);
        void Pick(Density d) => settings.Update(s => s with { Density = d });
        Options =
        [
            new DensityOption(Density.Comfortable, "Comfortable", ComfortableRowHeight, 6, Pick),
            new DensityOption(Density.Compact, "Compact", 12, 3, Pick),
        ];
        Sync(settings.Current);
    }

    /// <summary>
    /// The prototype sets 18 px rows, but four of them plus three 6 px gaps (90 px) do not fit the 76 px preview, so
    /// flexbox shrinks them to (76 − 18) / 4 = 14.5 px — which is what the reference render shows.
    /// </summary>
    public const double ComfortableRowHeight = 14.5;

    public IReadOnlyList<DensityOption> Options { get; }

    protected override void OnSync(AppSettings settings)
    {
        foreach (var option in Options)
        {
            option.IsSelected = option.Density == settings.Density;
        }
    }
}

/// <summary>A row whose control is a set of key caps (Windows › Quick add from anywhere).</summary>
public sealed class KeysRow(string key, string label, string description, IReadOnlyList<string> keys) : SettingsRow(key, label, description)
{
    public IReadOnlyList<string> Keys { get; } = keys;
}

/// <summary>An outline button in a settings row.</summary>
public sealed class RowButton(string label, IRelayCommand command)
{
    public string Label { get; } = label;

    public IRelayCommand Command { get; } = command;
}

/// <summary>A row whose control is one or more outline buttons (Open folder; Export, Import).</summary>
public sealed class ButtonsRow(string key, string label, string description, IReadOnlyList<RowButton> buttons) : SettingsRow(key, label, description)
{
    public IReadOnlyList<RowButton> Buttons { get; } = buttons;
}

/// <summary>A line of the Shortcuts tab: label and key caps.</summary>
public sealed record ShortcutRow(string Label, IReadOnlyList<string> Keys);
