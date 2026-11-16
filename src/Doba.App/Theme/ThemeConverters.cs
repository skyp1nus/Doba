using Avalonia;
using Avalonia.Data.Converters;

namespace Doba.App.Theme;

/// <summary>Small converters used by the control themes and available to views (via <c>x:Static</c>).</summary>
public static class ThemeConverters
{
    /// <summary><see cref="CornerRadius"/> → its top-left radius as a double (Rectangle.RadiusX/Y for dashed outlines).</summary>
    public static IValueConverter TopLeftRadius { get; } =
        new FuncValueConverter<CornerRadius, double>(r => r.TopLeft);

    /// <summary>Upper-cases a string (the prototype's <c>text-transform: uppercase</c> on mono labels).</summary>
    public static IValueConverter Upper { get; } =
        new FuncValueConverter<string?, string>(s => s?.ToUpperInvariant() ?? string.Empty);
}
