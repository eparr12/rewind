using Rewind.Core.Money;

namespace Rewind.Core.Tests.Money;

public sealed class DollarsTests
{
    // TEST-2: money is a rule, so each way the Store Manager might type a price has a case.
    [Theory]
    [InlineData("2", 200)]
    [InlineData("2.5", 250)]
    [InlineData("2.50", 250)]
    [InlineData("2.05", 205)]
    [InlineData("2.", 200)]
    [InlineData(".75", 75)]
    [InlineData("0.75", 75)]
    [InlineData("0", 0)]
    [InlineData("$2.50", 250)]
    [InlineData("$ 2.50", 250)]
    [InlineData("  2.50  ", 250)]
    [InlineData("002.50", 250)]
    [InlineData("99999.99", 9_999_999)]
    public void TryParse_Dollars_GivesWholeCents(string text, int cents)
    {
        Assert.True(Dollars.TryParse(text, out var parsed));
        Assert.Equal(cents, parsed);
    }

    // 100000 has six dollar digits, past the most Dollars reads, $99,999.99.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$")]
    [InlineData(".")]
    [InlineData("2.505")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("1,000")]
    [InlineData("2,50")]
    [InlineData("2 50")]
    [InlineData("1.2.3")]
    [InlineData("2.50$")]
    [InlineData("$$2")]
    [InlineData("2e2")]
    [InlineData("two")]
    [InlineData("100000")]
    [InlineData("٢.٥٠")] // Arabic-Indic digits (SEC-5)
    public void TryParse_NotDollars_IsFalse(string text) => Assert.False(Dollars.TryParse(text, out _));

    [Fact]
    public void TryParse_Null_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what TryParse does with one.
        Func<object?> parse = () => Dollars.TryParse(null!, out _);

        var refusal = Assert.Throws<ArgumentNullException>(parse);
        Assert.Equal("text", refusal.ParamName);
    }

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(75, "0.75")]
    [InlineData(200, "2.00")]
    [InlineData(205, "2.05")]
    [InlineData(9_999, "99.99")]
    public void Format_WholeCents_GivesDollarsAndCents(int cents, string text) => Assert.Equal(text, Dollars.Format(cents));

    [Fact]
    public void Format_NegativeCents_IsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => Dollars.Format(-1));

    // The Store Manager's boxes show a price as Format writes it, and a price saved as shown is the same price.
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(250)]
    [InlineData(9_999)]
    public void TryParse_WhatFormatWrites_GivesTheSameCents(int cents)
    {
        Assert.True(Dollars.TryParse(Dollars.Format(cents), out var parsed));
        Assert.Equal(cents, parsed);
    }
}
