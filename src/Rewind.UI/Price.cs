using Rewind.Core.Money;

namespace Rewind.UI;

/// <summary>How the screens show an amount of money in a line of text.</summary>
internal static class Price
{
    /// <summary>The amount with its dollar sign, such as $2.50 for 250 cents.</summary>
    public static string Of(int cents) => $"${Dollars.Format(cents)}";
}
