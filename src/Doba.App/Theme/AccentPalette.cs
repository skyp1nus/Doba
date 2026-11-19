using Avalonia.Media;
using Doba.Core.Model;

namespace Doba.App.Theme;

/// <summary>The four accent swatches of Settings › Appearance and the alpha variants the design derives from them.</summary>
public static class AccentPalette
{
    /// <summary>accent + '1A' (10 %): multi-day bar / schedule block fill.</summary>
    public const byte Alpha10 = 0x1A;

    /// <summary>accent + '26' (15 %): mini-calendar range strip.</summary>
    public const byte Alpha15 = 0x26;

    /// <summary>accent + '40' (25 %): multi-day bar / schedule block ring, text selection.</summary>
    public const byte Alpha25 = 0x40;

    /// <summary>accent + '29' (16 %): the dragged schedule copy / a resized block's fill.</summary>
    public const byte Alpha16 = 0x29;

    /// <summary>accent + '59' (35 %): a dragged block's dashed origin ring.</summary>
    public const byte Alpha35 = 0x59;

    /// <summary>accent + '73' (45 %): the dragged schedule copy / a resized block's ring.</summary>
    public const byte Alpha45 = 0x73;

    /// <summary>accent + 'B3' (70 %): a schedule block's resize grip.</summary>
    public const byte Alpha70 = 0xB3;

    public static Color Mist { get; } = Color.FromUInt32(0xFF9FB4C7);

    public static Color Sage { get; } = Color.FromUInt32(0xFFA3B8A0);

    public static Color Sand { get; } = Color.FromUInt32(0xFFC8B596);

    public static Color Gray { get; } = Color.FromUInt32(0xFFC4C4CA);

    public static Color ColorOf(Accent accent) => accent switch
    {
        Accent.Sage => Sage,
        Accent.Sand => Sand,
        Accent.Gray => Gray,
        _ => Mist,
    };

    /// <summary>Settings swatch label ("Mist", "Sage", "Sand", "Gray").</summary>
    public static string NameOf(Accent accent) => accent switch
    {
        Accent.Sage => "Sage",
        Accent.Sand => "Sand",
        Accent.Gray => "Gray",
        _ => "Mist",
    };

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
