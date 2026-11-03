namespace Rewind.Core.LateFees;

/// <summary>
/// Keeps late fees fair, which is the part the kids will check (Build Plan 3.2). Each member has a bag of
/// four cards, one Overdue and three Clear. A draw takes the next card, an empty bag is reshuffled, and a new
/// bag never starts with Overdue right after one, so nobody is hit twice in a row. This is the rule alone,
/// with no database and no clock.
/// </summary>
public static class ShuffleBag
{
    // Internal so the database's CHECK on a saved bag can be built from these, and can't drift from them.
    internal const int CardsInABag = 4;
    internal const char ClearLetter = 'C';
    internal const char OverdueLetter = 'O';

    /// <summary>Draws a member's next card.</summary>
    /// <param name="bag">What's left of the member's four cards, such as CCO. Empty when the bag is spent.</param>
    /// <param name="last">
    /// The last card the member drew, or null if they've never drawn one. It has to be the draw that left this
    /// bag, so the two are saved together (DATA-7): an Overdue still in the bag after an Overdue would hit the
    /// member twice in a row.
    /// </param>
    /// <param name="random">Where chance comes from (NET-8). It's used only to reshuffle a spent bag.</param>
    /// <returns>The card, and what's left of the bag for the member's next draw.</returns>
    /// <exception cref="ArgumentNullException">The bag or the random is null.</exception>
    /// <exception cref="ArgumentException">The bag has more than four cards, another letter, or two Overdue.</exception>
    public static (Card Card, string Left) Draw(string bag, Card? last, Random random)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(random);

        if (!IsABag(bag))
        {
            // NET-12: only a bug can get here, and a bug should be loud, not draw a Clear for each stray letter.
            throw new ArgumentException($"\"{bag}\" isn't what's left of a bag of {CardsInABag} cards.", nameof(bag));
        }

        if (bag.Length == 0)
        {
            bag = Fill(last, random);
        }

        var card = bag[0] == OverdueLetter ? Card.Overdue : Card.Clear;
        return (card, bag[1..]);
    }

    private static bool IsABag(string bag) =>
        bag.Length <= CardsInABag
        && bag.All(card => card is ClearLetter or OverdueLetter)
        && bag.Count(card => card == OverdueLetter) <= 1;

    private static string Fill(Card? last, Random random)
    {
        char[] cards = [OverdueLetter, ClearLetter, ClearLetter, ClearLetter]; // 1 in 4
        do
        {
            random.Shuffle(cards);
        }
        while (last == Card.Overdue && cards[0] == OverdueLetter); // never twice in a row

        return new string(cards);
    }
}
