using Rewind.Core.LateFees;

namespace Rewind.Core.Tests.LateFees;

public sealed class ShuffleBagTests
{
    private const int Seed = 42; // TEST-4: the seed fixes every shuffle, so nothing here passes by luck
    private const int Runs = 1_000;
    private const int CardsInABag = 4;
    private const int Leeway = 50; // out of Runs: enough to tell an even spread from a skewed one

    [Fact]
    public void Draw_ThousandDraws_NeverGivesOverdueTwiceInARow()
    {
        var cards = DrawInARow(Runs);

        Assert.DoesNotContain(cards.Zip(cards.Skip(1)), pair => pair is (Card.Overdue, Card.Overdue));
    }

    [Fact]
    public void Draw_ThousandDraws_HoldOneOverduePerBag()
    {
        var cards = DrawInARow(Runs);

        Assert.All(cards.Chunk(CardsInABag), bag => Assert.Single(bag, card => card == Card.Overdue));
        Assert.Equal(250, cards.Count(card => card == Card.Overdue));
    }

    [Fact]
    public void Draw_AfterBagEndsOnOverdue_NeverStartsWithOverdue()
    {
        var random = new Random(Seed);

        var firstCards = Enumerable.Range(0, Runs).Select(_ => ShuffleBag.Draw("", Card.Overdue, random).Card);

        Assert.DoesNotContain(Card.Overdue, firstCards);
    }

    // The reshuffle rule is only for a bag that follows Overdue. Any other bag stays fair: its Overdue card can
    // be anywhere, about a quarter of the time each, so a member can't count on the first night being safe.
    [Theory]
    [InlineData(null)]
    [InlineData(Card.Clear)]
    public void Draw_FreshBagNotAfterOverdue_PutsOverdueAnywhereEvenly(Card? last)
    {
        var positions = OverduePositions(last);

        Assert.All(positions, count => Assert.InRange(count, 250 - Leeway, 250 + Leeway));
    }

    // Reshuffling keeps the other three places even. Moving the Overdue card along one place would also never
    // start a bag with it, but the kids would soon learn the second night is the one to fear.
    [Fact]
    public void Draw_FreshBagAfterOverdue_PutsOverdueEvenlyAfterTheFirstCard()
    {
        var positions = OverduePositions(Card.Overdue);

        Assert.Equal(0, positions[0]);
        Assert.All(positions[1..], count => Assert.InRange(count, 333 - Leeway, 333 + Leeway));
    }

    // What's left is the member's bag from now on, so it has to be the same four cards, in the same letters.
    [Fact]
    public void Draw_EmptyBag_LeavesTheOtherThreeCards()
    {
        var (card, left) = ShuffleBag.Draw("", last: null, new Random(Seed));

        Assert.Equal(card == Card.Overdue ? "CCC" : "CCO", string.Concat(left.Order()));
    }

    [Theory]
    [InlineData("CCCO", Card.Clear, "CCO")] // a full bag
    [InlineData("CCO", Card.Clear, "CO")]
    [InlineData("OCC", Card.Overdue, "CC")]
    [InlineData("C", Card.Clear, "")]
    [InlineData("O", Card.Overdue, "")]
    public void Draw_BagWithCardsLeft_TakesTheFirstCard(string bag, Card card, string left)
    {
        var drawn = ShuffleBag.Draw(bag, last: null, new Random(Seed));

        Assert.Equal((card, left), drawn);
    }

    // NET-12: only a bug can hand over one of these, and a bug should be loud. Left alone, X would draw Clear,
    // and a run of Cs would draw a Clear for each.
    [Theory]
    [InlineData("X")] // not a card
    [InlineData("c")] // not in capitals
    [InlineData("CX")] // not a card, after one that is
    [InlineData("CCCCC")] // more than a full bag
    [InlineData("OO")] // two Overdue
    [InlineData("COCO")] // two Overdue, apart
    public void Draw_NotABag_IsRefused(string bag)
    {
        Action draw = () => ShuffleBag.Draw(bag, last: null, new Random(Seed));

        var refusal = Assert.Throws<ArgumentException>(draw);
        Assert.Equal("bag", refusal.ParamName);
    }

    [Fact]
    public void Draw_NullBag_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what Draw does with one.
        Action draw = () => ShuffleBag.Draw(null!, last: null, new Random(Seed));

        var refusal = Assert.Throws<ArgumentNullException>(draw);
        Assert.Equal("bag", refusal.ParamName);
    }

    [Fact]
    public void Draw_NullRandom_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what Draw does with one.
        Action draw = () => ShuffleBag.Draw("CCO", last: null, null!);

        var refusal = Assert.Throws<ArgumentNullException>(draw);
        Assert.Equal("random", refusal.ParamName);
    }

    // One member's draws, night after night, each from what the last one left.
    private static List<Card> DrawInARow(int draws)
    {
        var random = new Random(Seed);
        var cards = new List<Card>(draws);
        var bag = "";
        Card? last = null;
        for (var i = 0; i < draws; i++)
        {
            (var card, bag) = ShuffleBag.Draw(bag, last, random);
            cards.Add(card);
            last = card;
        }

        return cards;
    }

    // How many of a thousand fresh bags hold their Overdue card at each place, first to last.
    private static int[] OverduePositions(Card? last)
    {
        var random = new Random(Seed);
        var positions = new int[CardsInABag];
        for (var i = 0; i < Runs; i++)
        {
            var (card, left) = ShuffleBag.Draw("", last, random);
            positions[card == Card.Overdue ? 0 : left.IndexOf('O', StringComparison.Ordinal) + 1]++;
        }

        return positions;
    }
}
