using System.Globalization;

namespace Rewind.Core;

/// <summary>
/// Reads the numbers the clerk types, alike wherever they're typed: a movie's year and runtime at intake, and the
/// year a search of the catalog asks for (GEN-8). Blank is unknown, so null (DATA-3).
/// </summary>
internal static class TypedNumbers
{
    /// <summary>
    /// The earliest year a typed year can be: Roundhay Garden Scene's, the oldest film that survives. An earlier year
    /// is a slip.
    /// </summary>
    public const int FirstFilmYear = 1888;

    /// <summary>
    /// Reads a year in digits, from <see cref="FirstFilmYear"/> to next year on the register's own calendar. False
    /// when the text is neither blank nor such a year.
    /// </summary>
    public static bool TryReadYear(string? text, TimeProvider time, out int? year) =>
        TryReadWholeNumber(text, FirstFilmYear, time.GetLocalNow().Year + 1, out year); // NET-8

    /// <summary>
    /// Reads a whole number within its bounds. Spaces around it don't matter. NumberStyles.None takes the digits 0
    /// to 9 alone: no sign, separator or decimal point, and no other script's digits. False when the text is neither
    /// blank nor such a number.
    /// </summary>
    public static bool TryReadWholeNumber(string? text, int lowest, int highest, out int? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < lowest
            || value > highest)
        {
            return false;
        }

        number = value;
        return true;
    }
}
