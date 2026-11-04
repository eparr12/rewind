using Rewind.Core.Money;

namespace Rewind.Core.Tests.Money;

public sealed class ChangeTests
{
    [Fact]
    public void Break_FiveSeventyFive_IsAFiveAndThreeQuarters() =>
        Assert.Equal([(500, 1), (25, 3)], Change.Break(575));

    [Fact]
    public void Break_NoChange_IsNothing() =>
        Assert.Empty(Change.Break(0));

    // Every unit once, largest first: $20, $10, $5, $1, 25¢, 10¢, 5¢ and 1¢. Fake cash has no 50¢ or $50.
    [Fact]
    public void Break_OneOfEachUnit_ListsThemLargestFirst() =>
        Assert.Equal([(2000, 1), (1000, 1), (500, 1), (100, 1), (25, 1), (10, 1), (5, 1), (1, 1)], Change.Break(3641));

    [Fact]
    public void Break_FiftyDollars_IsTwentiesAndATen() =>
        Assert.Equal([(2000, 2), (1000, 1)], Change.Break(5000));

    [Fact]
    public void Break_FiftyCents_IsTwoQuarters() =>
        Assert.Equal([(25, 2)], Change.Break(50));

    [Fact]
    public void Break_BelowZero_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Change.Break(-1));

    // The wall sheet's example: count up from the total, coins first.
    [Fact]
    public void CountBack_FourTwentyFivePaidWithATen_CountsUpToTheTen() =>
        Assert.Equal([450, 475, 500, 1000], Change.CountBack(425, 1000));

    [Fact]
    public void CountBack_NoChange_SaysNothing() =>
        Assert.Empty(Change.CountBack(400, 400));

    // Build Plan 3.3's sale: $4.00 paid with a ten.
    [Fact]
    public void CountBack_SixDollarsChange_SaysFiveThenTen() =>
        Assert.Equal([500, 1000], Change.CountBack(400, 1000));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 10000)]
    [InlineData(399, 2000)]
    public void CountBack_AnyChange_EndsAtWhatWasPaid(int total, int paid) =>
        Assert.Equal(paid, Change.CountBack(total, paid)[^1]);

    [Fact]
    public void CountBack_PaidLessThanTheTotal_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Change.CountBack(1000, 425));
}
