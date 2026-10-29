using System.Globalization;

namespace Rewind.Core.Money;

/// <summary>
/// Money as it's typed and shown, in dollars and cents such as 2.50, turned into whole cents and back
/// (NET-9). Nothing here goes through decimal or double.
/// </summary>
public static class Dollars
{
    // Up to $99,999.99, far past any price. A cap on the digits also keeps the cents well inside an int.
    private const int MaxDollarDigits = 5;

    /// <summary>
    /// Reads an amount typed in dollars, such as 2, 2.5, 2.50, .75 or $2.50, as whole cents. False for
    /// anything else, such as 2.505, -1, 1,000, two or a blank.
    /// </summary>
    public static bool TryParse(string text, out int cents)
    {
        ArgumentNullException.ThrowIfNull(text);

        cents = 0;
        var amount = text.AsSpan().Trim();
        if (amount.StartsWith('$'))
        {
            amount = amount[1..].TrimStart();
        }

        var point = amount.IndexOf('.');
        var whole = point < 0 ? amount : amount[..point];
        var fraction = point < 0 ? [] : amount[(point + 1)..];
        if (whole.Length + fraction.Length == 0 || whole.Length > MaxDollarDigits || fraction.Length > 2
            || !IsDigits(whole) || !IsDigits(fraction))
        {
            return false;
        }

        // One digit after the point is tenths: 2.5 is 2.50.
        cents = (Number(whole) * 100) + (Number(fraction) * (fraction.Length == 1 ? 10 : 1));
        return true;
    }

    /// <summary>Writes whole cents in dollars and cents, such as 2.50 for 250, with no dollar sign.</summary>
    public static string Format(int cents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cents);
        return string.Create(CultureInfo.InvariantCulture, $"{cents / 100}.{cents % 100:00}");
    }

    private static bool IsDigits(ReadOnlySpan<char> text) => !text.ContainsAnyExceptInRange('0', '9');

    private static int Number(ReadOnlySpan<char> digits) =>
        digits.IsEmpty ? 0 : int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
}
