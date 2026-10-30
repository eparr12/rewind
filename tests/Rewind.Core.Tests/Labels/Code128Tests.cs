using System.Xml.Linq;
using Rewind.Core.Labels;
using Rewind.Core.Scans;

namespace Rewind.Core.Tests.Labels;

public sealed class Code128Tests
{
    // The narrowest bar a scanner reads reliably: 10 mil, a hundredth of an inch.
    private const double TenMil = 0.010;

    // The least white Code 128 needs either side of its bars, in modules (ISO/IEC 15417).
    private const int LeastQuietZoneModules = 10;

    // CON- and the 20 letters Barcodes allows a concession code, 24 characters in all.
    private const string LongestConcessionCode = "CON-ABCDEFGHIJKLMNOPQRST";

    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    // Each kind of code Rewind prints, at both ends of its numbers, and the concession menu that Build
    // Plan 3.1 prints from the Store Manager.
    public static TheoryData<string> AnyCode => new()
    {
        "RV-00001",
        "RV-99999",
        "MEM-001",
        "MEM-999",
        "CON-POPCORN",
    };

    [Theory]
    [InlineData("RV-00001")]
    [InlineData("RV-99999")]
    public void Svg_StoreLabel_DecodesBackAsTheSameCode(string code)
    {
        var svg = Code128.Svg(code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var scan = Barcodes.Classify(DrawnBarcode.Read(svg).Decode());
        Assert.Equal(new Scan(ScanKind.StoreLabel, code), scan);
    }

    [Theory]
    [InlineData("MEM-001")]
    [InlineData("MEM-999")]
    public void Svg_MemberCard_DecodesBackAsTheSameCode(string code)
    {
        var svg = Code128.Svg(code, Code128.MemberCardWidthInches, Code128.MemberCardHeightInches);

        var scan = Barcodes.Classify(DrawnBarcode.Read(svg).Decode());
        Assert.Equal(new Scan(ScanKind.MemberCard, code), scan);
    }

    // Build Plan 3.1 prints a laminated concession menu, with barcodes of its own size (#21).
    [Theory]
    [InlineData("CON-POPCORN")]
    [InlineData(LongestConcessionCode)]
    public void Svg_Concession_DecodesBackAsTheSameCode(string code)
    {
        var svg = Code128.Svg(code, Code128.MenuWidthInches, Code128.MenuHeightInches);

        var scan = Barcodes.Classify(DrawnBarcode.Read(svg).Decode());
        Assert.Equal(new Scan(ScanKind.Concession, code), scan);
    }

    // A page takes the barcode as markup, so nothing may come before or after the svg element.
    [Theory]
    [MemberData(nameof(AnyCode))]
    public void Svg_AnyCode_IsOnlyTheSvgElement(string code)
    {
        var svg = Code128.Svg(code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<?", svg, StringComparison.Ordinal); // an XML declaration
        Assert.DoesNotContain("<!", svg, StringComparison.Ordinal); // a DOCTYPE or a comment
        Assert.Equal(SvgNamespace + "svg", XElement.Parse(svg).Name); // one element, and an svg
    }

    // A sheet lays out its labels and cards around their barcodes' printed size.
    [Theory]
    [MemberData(nameof(AnyCode))]
    public void Svg_AnyCode_PrintsAtTheSizeAskedFor(string code)
    {
        var svg = Code128.Svg(code, 2.5, 0.75);

        var root = XElement.Parse(svg);
        Assert.Equal(("2.5in", "0.75in"), ((string?)root.Attribute("width"), (string?)root.Attribute("height")));
    }

    // ZXing.Net would draw an empty code as a checksum and a stop with no start, which nothing reads.
    [Fact]
    public void Svg_EmptyCode_IsRefused()
    {
        var draw = () => Code128.Svg("", Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var refusal = Assert.Throws<ArgumentException>(draw);
        Assert.Equal("code", refusal.ParamName);
    }

    // ZXing.Net would throw a NullReferenceException, which says nothing about whose null it was.
    [Fact]
    public void Svg_NullCode_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what Svg does with one.
        var draw = () => Code128.Svg(null!, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var refusal = Assert.Throws<ArgumentNullException>(draw);
        Assert.Equal("code", refusal.ParamName);
    }

    // Every module stretches to fit the width, so a code too long for it would print bars too narrow to
    // scan: 30 characters at a store label's width are 385 modules, 5.7 mil each.
    [Fact]
    public void Svg_CodeTooLongForItsWidth_IsRefused()
    {
        var draw = () => Code128.Svg(new string('A', 30), Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(draw);
        Assert.Equal("widthInches", refusal.ParamName);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.2)]
    public void Svg_WidthOfZeroOrLess_IsRefused(double widthInches)
    {
        var draw = () => Code128.Svg("RV-00001", widthInches, Code128.StoreLabelHeightInches);

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(draw);
        Assert.Equal("widthInches", refusal.ParamName);
    }

    // No comparison is true of NaN, so it would pass a floor that only compares, and print as "NaNin".
    [Fact]
    public void Svg_WidthNotANumber_IsRefused()
    {
        var draw = () => Code128.Svg("RV-00001", double.NaN, Code128.StoreLabelHeightInches);

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(draw);
        Assert.Equal("widthInches", refusal.ParamName);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.45)]
    public void Svg_HeightOfZeroOrLess_IsRefused(double heightInches)
    {
        var draw = () => Code128.Svg("RV-00001", Code128.StoreLabelWidthInches, heightInches);

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(draw);
        Assert.Equal("heightInches", refusal.ParamName);
    }

    // Build Plan 2.5's check is a label that scans on the first try, and a scanner reads bars of 10 mil
    // or more.
    [Theory]
    [InlineData("RV-00001")]
    [InlineData("RV-99999")]
    public void Svg_StoreLabel_PrintsBarsAtLeastTenMil(string code)
    {
        var svg = Code128.Svg(code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var barcode = DrawnBarcode.Read(svg);
        var narrowest = barcode.Bars.Min(bar => bar.Width) * barcode.ModuleInches;
        Assert.True(narrowest >= TenMil, $"The narrowest bar prints {narrowest} inch wide.");
    }

    [Theory]
    [InlineData("MEM-001")]
    [InlineData("MEM-999")]
    public void Svg_MemberCard_PrintsBarsAtLeastTenMil(string code)
    {
        var svg = Code128.Svg(code, Code128.MemberCardWidthInches, Code128.MemberCardHeightInches);

        var barcode = DrawnBarcode.Read(svg);
        var narrowest = barcode.Bars.Min(bar => bar.Width) * barcode.ModuleInches;
        Assert.True(narrowest >= TenMil, $"The narrowest bar prints {narrowest} inch wide.");
    }

    // #21: the longest code Barcodes reads as a concession, 24 characters, at the menu's width. A member
    // card's would refuse it.
    [Theory]
    [InlineData("CON-POPCORN")]
    [InlineData(LongestConcessionCode)]
    public void Svg_Concession_PrintsBarsAtLeastTenMil(string code)
    {
        var svg = Code128.Svg(code, Code128.MenuWidthInches, Code128.MenuHeightInches);

        var barcode = DrawnBarcode.Read(svg);
        var narrowest = barcode.Bars.Min(bar => bar.Width) * barcode.ModuleInches;
        Assert.True(narrowest >= TenMil, $"The narrowest bar prints {narrowest} inch wide.");
    }

    // Without the white either side, a scanner can miss where the barcode starts or ends. ZXing's
    // reader doesn't need it at the ends of a line, so the decode tests pass without it: this measures it.
    [Theory]
    [MemberData(nameof(AnyCode))]
    public void Svg_AnyCode_HasTenModuleQuietZones(string code)
    {
        var svg = Code128.Svg(code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var barcode = DrawnBarcode.Read(svg);
        var before = barcode.Bars[0].X;
        var after = barcode.Modules - (barcode.Bars[^1].X + barcode.Bars[^1].Width);
        Assert.True(
            before >= LeastQuietZoneModules && after >= LeastQuietZoneModules,
            $"{before} modules of white before the bars, and {after} after them.");
    }

    // The viewBox is one unit tall. Kept to its shape, the drawing would shrink until its width fit,
    // leaving bars as tall as one module is wide. Stretched, the bars fill the printed height, and
    // their widths stay exact.
    [Theory]
    [MemberData(nameof(AnyCode))]
    public void Svg_AnyCode_StretchesItsBarsToThePrintedHeight(string code)
    {
        var svg = Code128.Svg(code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

        var root = XElement.Parse(svg);
        var viewHeight = ((string?)root.Attribute("viewBox"))?.Split(' ')[3];
        Assert.Equal("none", (string?)root.Attribute("preserveAspectRatio"));
        Assert.All(root.Elements(SvgNamespace + "rect"), rect => Assert.Equal(viewHeight, (string?)rect.Attribute("height")));
    }
}
