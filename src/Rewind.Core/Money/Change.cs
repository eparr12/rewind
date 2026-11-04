namespace Rewind.Core.Money;

/// <summary>
/// How to give change in the store's fake cash (wall sheet R4). Lists rather than lazy sequences, so a screen
/// that draws twice can't run them twice.
/// </summary>
public static class Change
{
    // $20 down to 1¢. Fake cash has no fifties and no half dollars.
    private static readonly int[] Units = [2000, 1000, 500, 100, 25, 10, 5, 1];

    /// <summary>
    /// The change as bills and coins, largest first: 575 is 1 × 500 and 3 × 25. No change is an empty list.
    /// </summary>
    public static IReadOnlyList<(int UnitCents, int Count)> Break(int changeCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(changeCents);

        var breakdown = new List<(int UnitCents, int Count)>();
        var left = changeCents;
        foreach (var unit in Units)
        {
            if (left >= unit)
            {
                breakdown.Add((unit, left / unit));
                left %= unit;
            }
        }

        return breakdown;
    }

    /// <summary>
    /// What the clerk says, counting the change back up from the total, coins first: a total of 425 paid with
    /// 1000 says 450, 475, 500, 1000. No change is an empty list.
    /// </summary>
    public static IReadOnlyList<int> CountBack(int totalCents, int paidCents)
    {
        var counted = new List<int>();
        var said = totalCents;
        foreach (var (unit, count) in Break(paidCents - totalCents).Reverse())
        {
            for (var i = 0; i < count; i++)
            {
                said += unit;
                counted.Add(said);
            }
        }

        return counted;
    }
}
