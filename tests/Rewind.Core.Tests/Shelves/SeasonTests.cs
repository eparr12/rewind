using System.Globalization;
using Rewind.Core.Shelves;

namespace Rewind.Core.Tests.Shelves;

public sealed class SeasonTests
{
    // Summer, June 21 to September 22: both its days are in it, and the day either side of each isn't. August 1 is in
    // it too, though its day of the month is outside 21 to 22, so the months count, not the days alone.
    [Theory]
    [InlineData(6, 20, false)]
    [InlineData(6, 21, true)]
    [InlineData(8, 1, true)]
    [InlineData(9, 22, true)]
    [InlineData(9, 23, false)]
    public void Holds_DayNearOrInASummerSeason_TakesInBothEnds(int month, int day, bool holds)
    {
        var summer = new Season(6, 21, 9, 22);

        Assert.Equal(holds, summer.Holds(new DateOnly(2026, month, day)));
    }

    // December 1 to January 6, Twelfth Night, as ADR-0015 has it: in it on both sides of January 1, and out of it before
    // the first day, after the last and in the middle of the year.
    [Theory]
    [InlineData(11, 30, false)]
    [InlineData(12, 1, true)]
    [InlineData(12, 31, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 6, true)]
    [InlineData(1, 7, false)]
    [InlineData(7, 4, false)]
    public void Holds_SeasonOverNewYear_RunsOnPastDecember31(int month, int day, bool holds)
    {
        var christmas = new Season(12, 1, 1, 6);

        Assert.Equal(holds, christmas.Holds(new DateOnly(2026, month, day)));
    }

    // Halloween alone, October 31 to October 31: a first day that's also the last isn't a season over New Year.
    [Theory]
    [InlineData(10, 30, false)]
    [InlineData(10, 31, true)]
    [InlineData(11, 1, false)]
    public void Holds_OneDaySeason_HoldsThatDayOnly(int month, int day, bool holds)
    {
        var halloween = new Season(10, 31, 10, 31);

        Assert.Equal(holds, halloween.Holds(new DateOnly(2026, month, day)));
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 7, 4)]
    [InlineData(2026, 12, 31)]
    [InlineData(2028, 2, 29)]
    public void Holds_WholeYear_HoldsEveryDay(int year, int month, int day)
    {
        var allYear = new Season(1, 1, 12, 31);

        Assert.True(allYear.Holds(new DateOnly(year, month, day)));
    }

    // February 1 to February 29 ends on February 28 in a year without the 29th, with no special case: March 1 comes
    // after February 29 whatever the year (ADR-0015). 2027 has no February 29, and 2028 does.
    [Theory]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2027, 3, 1, false)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2028, 3, 1, false)]
    public void Holds_SeasonEndingOnFebruary29_EndsOnThe28thInAYearWithoutIt(int year, int month, int day, bool holds)
    {
        var february = new Season(2, 1, 2, 29);

        Assert.Equal(holds, february.Holds(new DateOnly(year, month, day)));
    }

    // February 29 to March 31 starts on March 1 in a year without the 29th, as ADR-0015's consequence says.
    [Theory]
    [InlineData(2027, 2, 28, false)]
    [InlineData(2027, 3, 1, true)]
    [InlineData(2028, 2, 28, false)]
    [InlineData(2028, 2, 29, true)]
    public void Holds_SeasonStartingOnFebruary29_StartsOnMarch1InAYearWithoutIt(int year, int month, int day, bool holds)
    {
        var leapDayOn = new Season(2, 29, 3, 31);

        Assert.Equal(holds, leapDayOn.Holds(new DateOnly(year, month, day)));
    }

    // As the family says it, and as the log and F5's Shelves tab write it.
    [Fact]
    public void Describe_Halloween_WritesBothDaysInWords()
    {
        var halloween = new Season(10, 1, 10, 31);

        Assert.Equal("October 1 to October 31", halloween.Describe());
    }

    // The first day first, though it comes later in the year than the last.
    [Fact]
    public void Describe_SeasonOverNewYear_WritesTheFirstDayFirst()
    {
        var christmas = new Season(12, 1, 1, 6);

        Assert.Equal("December 1 to January 6", christmas.Describe());
    }

    // The log reads alike on any PC, so a season's months are in English on one set to French too.
    [Fact]
    public void Describe_PcInAnotherLanguage_WritesTheMonthsInEnglish()
    {
        var halloween = new Season(10, 1, 10, 31);
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        string described;
        try
        {
            described = halloween.Describe();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal("October 1 to October 31", described);
    }

    // The twelve the Shelves tab's lists offer, in order, as Core numbers them.
    [Fact]
    public void MonthName_EachMonth_IsItsName()
    {
        var names = Enumerable.Range(1, 12).Select(Season.MonthName);

        Assert.Equal(
            ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
            names);
    }

    // GetMonthName would write a 13th month as nothing, so the one writer of a month's name refuses it, as it does 0.
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void MonthName_NotAMonth_Throws(int month)
    {
        var name = () => Season.MonthName(month);

        var bug = Assert.Throws<ArgumentOutOfRangeException>(name);
        Assert.Equal("month", bug.ParamName);
    }
}
