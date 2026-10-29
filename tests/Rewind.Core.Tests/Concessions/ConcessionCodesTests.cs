using Rewind.Core.Concessions;
using Rewind.Core.Scans;

namespace Rewind.Core.Tests.Concessions;

public sealed class ConcessionCodesTests
{
    // Fountain pop or SodaStream has 23 letters, so its code stops at the 20th.
    [Theory]
    [InlineData("Popcorn", "CON-POPCORN")]
    [InlineData("Fountain pop", "CON-FOUNTAINPOP")]
    [InlineData("M&M's", "CON-MMS")]
    [InlineData("7 Up", "CON-UP")]
    [InlineData("Jalapeño poppers", "CON-JALAPENOPOPPERS")]
    [InlineData("Fountain pop or SodaStream", "CON-FOUNTAINPOPORSODASTR")]
    public void Suggest_Name_GivesItsLettersInCapitals(string name, string code) => Assert.Equal(code, ConcessionCodes.Suggest(name));

    // SEC-5: a bad paste can leave half a surrogate pair, which would make Normalize throw on a keystroke
    // and show the error dialog. It's read as U+FFFD, as WebAnswers reads typed text, and has no letter. A
    // fact, not a theory row: a theory's data is serialized, which could mend the pair before the test.
    [Fact]
    public void Suggest_NameWithHalfASurrogatePair_SkipsIt() => Assert.Equal("CON-POPCORN", ConcessionCodes.Suggest("Pop\uD800corn"));

    // Nothing to make a code from, so the Store Manager finishes it.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    public void Suggest_NoLetters_GivesThePrefixAlone(string name) => Assert.Equal("CON-", ConcessionCodes.Suggest(name));

    // A suggestion with letters is a code the register reads as a snack, so the Store Manager can keep it.
    [Theory]
    [InlineData("Popcorn")]
    [InlineData("Jalapeño poppers")]
    [InlineData("Fountain pop or SodaStream")]
    public void Suggest_NameWithLetters_IsACodeBarcodesReadsAsAConcession(string name) =>
        Assert.Equal(ScanKind.Concession, Barcodes.Classify(ConcessionCodes.Suggest(name)).Kind);

    [Fact]
    public void Suggest_Null_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what Suggest does with one.
        Func<object?> suggest = () => ConcessionCodes.Suggest(null!);

        var refusal = Assert.Throws<ArgumentNullException>(suggest);
        Assert.Equal("name", refusal.ParamName);
    }
}
