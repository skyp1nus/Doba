using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Doba.App.Theme;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Screens.Week;

/// <summary>
/// Measures week cards the way the view will lay them out, so Core's "+N more" fitting sees real heights: the title
/// is shaped with the real font (Font.Sans Medium, FontSize.13, LineHeight.Card) at the width the card leaves it
/// (<see cref="Room"/>: card width − density padding − checkbox − two density gaps − the meta's width, itself measured
/// in Font.Mono FontSize.10.5), broken between words only and clamped to two lines; height =
/// max(Density.CardMinHeight, padding + lines × line height). Results are cached per (title, meta) until the width or
/// the density changes. <see cref="Room"/> is also what <c>TaskCard</c> lays itself out with, so the two agree on
/// when a narrow column drops the tag.
/// </summary>
internal sealed class WeekCardMeasurer
{
    private const int MaxCacheEntries = 4096;

    // Meta text widths do not depend on the card width or the density: shared by every measurer and card (UI thread).
    private static readonly Dictionary<string, double> s_metaWidths = new(StringComparer.Ordinal);
    private static Typeface s_metaFace;
    private static FontFeatureCollection? s_features;
    private static double s_metaSize;
    private static bool s_metaResolved;

    private readonly Dictionary<CardKey, double> _cards = [];

    private bool _resolved;
    private Typeface _titleFace;
    private double _titleSize;
    private double _lineHeight;

    private Thickness _padding;
    private double _minHeight;
    private double _gap;

    /// <summary>Outer width of a card (column width minus the list padding).</summary>
    public double CardWidth { get; private set; } = double.NaN;

    public Density Density { get; private set; }

    /// <summary>
    /// Width left for the title in a card <paramref name="cardWidth"/> wide, and whether the tag is shown: when the
    /// title would get less than <see cref="WeekLayout.MinTitleWidth"/>, the tag is dropped (priority and time stay).
    /// </summary>
    /// <param name="tagLabel">"#work", or null.</param>
    /// <param name="timeText">"16:30", or null.</param>
    public static (double TitleWidth, bool ShowTag) Room(double cardWidth, Thickness padding, double gap,
        bool hasPriority, string? tagLabel, string? timeText)
    {
        var meta = 0.0;
        var items = 0;
        if (hasPriority)
        {
            meta += WeekLayout.PriorityButton;
            items++;
        }

        if (!string.IsNullOrEmpty(timeText))
        {
            meta += MetaWidth(timeText);
            items++;
        }

        var bare = cardWidth - padding.Left - padding.Right - WeekLayout.CheckSize - (2 * gap);
        var withoutTag = bare - meta - (items > 1 ? (items - 1) * WeekLayout.MetaGap : 0);
        if (string.IsNullOrEmpty(tagLabel))
        {
            return (withoutTag, false);
        }

        var withTag = withoutTag - MetaWidth(tagLabel) - (items > 0 ? WeekLayout.MetaGap : 0);
        return withTag >= WeekLayout.MinTitleWidth ? (withTag, true) : (withoutTag, false);
    }

    /// <summary>Sets the card width and density; clears the cache when either changed.</summary>
    public void Configure(double cardWidth, Density density)
    {
        if (cardWidth == CardWidth && density == Density && _resolved)
        {
            return;
        }

        CardWidth = cardWidth;
        Density = density;
        _cards.Clear();
        Resolve(density);
    }

    /// <summary>Height of the card for a task with this title and meta (raw tag without '#').</summary>
    public double Height(string title, string? tag, TimeOnly? time, bool hasPriority)
    {
        var key = new CardKey(title, tag, time, hasPriority);
        if (_cards.TryGetValue(key, out var h))
        {
            return h;
        }

        if (_cards.Count >= MaxCacheEntries)
        {
            _cards.Clear();
        }

        var (available, _) = Room(CardWidth, _padding, _gap, hasPriority,
            string.IsNullOrEmpty(tag) ? null : "#" + tag, time is { } t ? DateMath.Hhmm(t) : null);
        var lines = Math.Min(2, TitleLines(title, available));
        h = Math.Max(_minHeight, _padding.Top + _padding.Bottom + (lines * _lineHeight));
        _cards[key] = h;
        return h;
    }

    private int TitleLines(string title, double width)
    {
        if (width <= 1)
        {
            return 2;
        }

        using var layout = new TextLayout(title, _titleFace, _titleSize, null,
            textWrapping: TextWrapping.WrapWithOverflow, maxWidth: width, lineHeight: _lineHeight, fontFeatures: s_features);
        return Math.Max(1, layout.TextLines.Count);
    }

    private static double MetaWidth(string text)
    {
        if (s_metaWidths.TryGetValue(text, out var w))
        {
            return w;
        }

        ResolveMeta();
        using var layout = new TextLayout(text, s_metaFace, s_metaSize, null, fontFeatures: s_features);

        // Layout rounding may round the text block up to the next pixel.
        w = Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
        if (s_metaWidths.Count >= MaxCacheEntries)
        {
            s_metaWidths.Clear();
        }

        s_metaWidths[text] = w;
        return w;
    }

    private static void ResolveMeta()
    {
        if (s_metaResolved)
        {
            return;
        }

        s_metaFace = new Typeface(Resource("Font.Mono", FontFamily.Default));
        s_features = Resource<FontFeatureCollection?>("Font.Features", null);
        s_metaSize = Resource("FontSize.10.5", 10.5);
        s_metaResolved = true;
    }

    private void Resolve(Density density)
    {
        foreach (var (key, comfortable, compact) in ThemeService.DensityTable)
        {
            var value = density == Density.Compact ? compact : comfortable;
            switch (key)
            {
                case "Density.CardPadding":
                    _padding = (Thickness)value;
                    break;
                case "Density.CardMinHeight":
                    _minHeight = (double)value;
                    break;
                case "Density.CardGap":
                    _gap = (double)value;
                    break;
            }
        }

        if (_resolved)
        {
            return;
        }

        ResolveMeta();
        _titleFace = new Typeface(Resource("Font.Sans", FontFamily.Default), FontStyle.Normal, FontWeight.Medium);
        _titleSize = Resource("FontSize.13", 13.0);
        _lineHeight = Resource("LineHeight.Card", 18.0);
        _resolved = true;
    }

    private static T Resource<T>(string key, T fallback) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is T typed ? typed : fallback;

    private readonly record struct CardKey(string Title, string? Tag, TimeOnly? Time, bool HasPriority);
}
