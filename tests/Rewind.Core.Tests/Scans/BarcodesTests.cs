using Rewind.Core.Scans;

namespace Rewind.Core.Tests.Scans;

public sealed class BarcodesTests
{
    [Theory]
    [InlineData("RV-00042", ScanKind.StoreLabel)]
    [InlineData("MEM-001", ScanKind.MemberCard)]
    [InlineData("CON-POPCORN", ScanKind.Concession)]
    [InlineData("036000291452", ScanKind.Upc)]
    public void Classify_RewindBarcode_SortsItByPrefix(string raw, ScanKind expected)
    {
        var scan = Barcodes.Classify(raw);

        Assert.Equal(expected, scan.Kind);
    }

    [Fact]
    public void Classify_LowerCaseLabel_ReturnsItUpperCase()
    {
        var scan = Barcodes.Classify("rv-00042");

        Assert.Equal(new Scan(ScanKind.StoreLabel, "RV-00042"), scan);
    }

    [Fact]
    public void Classify_SpacesAround_IgnoresThem()
    {
        var scan = Barcodes.Classify(" MEM-001 ");

        Assert.Equal(new Scan(ScanKind.MemberCard, "MEM-001"), scan);
    }

    [Fact]
    public void Classify_EanStartingWithZero_ReturnsTwelveDigitUpc()
    {
        var scan = Barcodes.Classify("0036000291452");

        Assert.Equal(new Scan(ScanKind.Upc, "036000291452"), scan);
    }

    [Fact]
    public void Classify_ThirteenDigitEan_KeepsAllThirteenDigits()
    {
        var scan = Barcodes.Classify("4006381333931");

        Assert.Equal(new Scan(ScanKind.Upc, "4006381333931"), scan);
    }

    [Fact]
    public void Classify_ExpandedUpcE_KeepsAllTwelveDigits()
    {
        // UPC-E 04252614 as the scanner sends it with UPC-E0 Expand on (README). Like every
        // expanded UPC-E0 it starts with 0, which only a 13-digit code loses.
        var scan = Barcodes.Classify("042100005264");

        Assert.Equal(new Scan(ScanKind.Upc, "042100005264"), scan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HELLO")]
    [InlineData("036000291453")] // wrong check digit
    [InlineData("04252614")] // UPC-E: the scanner sends it expanded to UPC-A (README)
    [InlineData("RV-0042")] // four digits
    [InlineData("MEM-0001")] // four digits
    [InlineData("CON-")] // no snack
    [InlineData("RV-٠٠٠٤٢")] // Arabic-Indic digits (SEC-5)
    public void Classify_NotARewindBarcode_IsUnknown(string raw)
    {
        var scan = Barcodes.Classify(raw);

        Assert.Equal(ScanKind.Unknown, scan.Kind);
    }

    [Fact]
    public void Classify_LongerThanMaxLength_IsUnknownAndNotKept()
    {
        var scan = Barcodes.Classify(new string('1', Barcodes.MaxLength + 1));

        Assert.Equal(new Scan(ScanKind.Unknown, ""), scan);
    }
}
