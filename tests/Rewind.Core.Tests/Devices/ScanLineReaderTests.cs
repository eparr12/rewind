using System.Text;
using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.Core.Tests.Devices;

public sealed class ScanLineReaderTests
{
    [Theory]
    [InlineData("RV-00042\r")]
    [InlineData("RV-00042\n")]
    [InlineData("RV-00042\r\n")]
    public void Feed_AnyLineEnding_FinishesTheScanAtOnce(string sent)
    {
        var reader = new ScanLineReader();

        var scans = reader.Feed(Ascii(sent));

        Assert.Equal("RV-00042", Assert.Single(scans));
    }

    [Fact]
    public void Feed_LfArrivingAfterCr_AddsNothing()
    {
        var reader = new ScanLineReader();
        reader.Feed(Ascii("RV-00042\r"));

        var scans = reader.Feed(Ascii("\n"));

        Assert.Empty(scans);
    }

    [Fact]
    public void Feed_ScanSplitAcrossReads_JoinsIt()
    {
        var reader = new ScanLineReader();
        reader.Feed(Ascii("RV-00"));

        var scans = reader.Feed(Ascii("042\r"));

        Assert.Equal("RV-00042", Assert.Single(scans));
    }

    [Fact]
    public void Feed_TwoScansInOneRead_ReturnsBothInOrder()
    {
        var reader = new ScanLineReader();

        var scans = reader.Feed(Ascii("RV-00042\rMEM-001\r"));

        Assert.Collection(
            scans,
            first => Assert.Equal("RV-00042", first),
            second => Assert.Equal("MEM-001", second));
    }

    [Fact]
    public void Feed_EmptyLines_AreSkipped()
    {
        var reader = new ScanLineReader();

        var scans = reader.Feed(Ascii("\r\n\r\nMEM-001\r\n"));

        Assert.Equal("MEM-001", Assert.Single(scans));
    }

    [Fact]
    public void Feed_NoLineEndingYet_FinishesNothing()
    {
        var reader = new ScanLineReader();

        var scans = reader.Feed(Ascii("RV-00042"));

        Assert.Empty(scans);
    }

    [Fact]
    public void Feed_LineLongerThanMax_IsCutToAnUnknownScan()
    {
        var reader = new ScanLineReader();
        var sent = "RV-00042" + new string(' ', ScanLineReader.MaxLineLength) + "\r";

        var scans = reader.Feed(Ascii(sent));

        Assert.Equal(ScanKind.Unknown, Barcodes.Classify(Assert.Single(scans)).Kind);
    }

    [Fact]
    public void Feed_LineLongerThanMax_DropsTheRest()
    {
        var reader = new ScanLineReader();
        var kept = new string('1', ScanLineReader.MaxLineLength);

        var scans = reader.Feed(Ascii(kept + "2222\r"));

        Assert.Equal(kept, Assert.Single(scans));
    }

    [Fact]
    public void Feed_ByteOutsidePrintableAscii_BecomesQuestionMark()
    {
        var reader = new ScanLineReader();
        byte[] sent = [(byte)'R', 0xE9, (byte)'\r'];

        var scans = reader.Feed(sent);

        Assert.Equal("R?", Assert.Single(scans));
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
