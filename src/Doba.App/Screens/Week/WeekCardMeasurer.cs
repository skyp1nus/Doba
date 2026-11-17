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
/// (card width − density padding − checkbox − two density gaps − the meta's width, itself measured in Font.Mono
/// FontSize.10.5), clamped to two lines; height = max(Density.CardMinHeight, padding + lines × line height).
/// Results are cached per (title, meta) until the width or the density changes.
/// </summary>
internal sealed class WeekCardMeasurer
{
    private const int MaxCacheEntries = 4096;

    private readonly Dictionary<CardKey, double> _cards = [];
    private readonly Dictionary<string, double> _metaWidths = new(StringComparer.Ordinal);

    private bool _resolved;
    private Typeface _titleFace;
    private Typeface _metaFace;
    private FontFeatureCollection? _features;
    private double _titleSize;
    private double _lineHeight;
    private double _metaSize;

    private Thickness _padding;
    private double _minHeight;
    private double _gap;

    /// <summary>Outer width of a card (column width minus the list padding).</summary>
    public double CardWidth { get; private set; } = double.NaN;

    public Density Density { get; private set; }

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

        var meta = 0.0;
        var items = 0;
        if (hasPriority)
        {
            meta += WeekLayout.PriorityButton;
            items++;
        }

        if (!string.IsNullOrEmpty(tag))
        {
            meta += MetaWidth("#" + tag);
            items++;
        }

        if (time is { } t)
        {
            meta += MetaWidth(DateMath.Hhmm(t));
            items++;
        }

        if (items > 1)
        {
            meta += (items - 1) * WeekLayout.MetaGap;
        }

        var available = CardWidth - _padding.Left - _padding.Right - WeekLayout.CheckSize - (2 * _gap) - meta;
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
            textWrapping: TextWrapping.Wrap, maxWidth: width, lineHeight: _lineHeight, fontFeatures: _features);
        return Math.Max(1, layout.TextLines.Count);
    }

    private double MetaWidth(string text)
    {
        if (_metaWidths.TryGetValue(text, out var w))
        {
            return w;
        }

        using var layout = new TextLayout(text, _metaFace, _metaSize, null, fontFeatures: _features);

        // Layout rounding may round the text block up to the next pixel.
        w = Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
        if (_metaWidths.Count >= MaxCacheEntries)
        {
            _metaWidths.Clear();
        }

        _metaWidths[text] = w;
        return w;
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

        _titleFace = new Typeface(Resource("Font.Sans", FontFamily.Default), FontStyle.Normal, FontWeight.Medium);
        _metaFace = new Typeface(Resource("Font.Mono", FontFamily.Default));
        _features = Resource<FontFeatureCollection?>("Font.Features", null);
        _titleSize = Resource("FontSize.13", 13.0);
        _lineHeight = Resource("LineHeight.Card", 18.0);
        _metaSize = Resource("FontSize.10.5", 10.5);
        _resolved = true;
    }

    private static T Resource<T>(string key, T fallback) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is T typed ? typed : fallback;

    private readonly record struct CardKey(string Title, string? Tag, TimeOnly? Time, bool HasPriority);
}
