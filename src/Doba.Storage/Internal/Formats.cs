using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Doba.Storage.Internal;

/// <summary>The text formats used in the database and in JSON: dates "yyyy-MM-dd", times "HH:mm".</summary>
internal static class Formats
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static bool TryParseDate([NotNullWhen(true)] string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool TryParseTime([NotNullWhen(true)] string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
