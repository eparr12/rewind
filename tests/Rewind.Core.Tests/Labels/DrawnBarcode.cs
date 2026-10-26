using System.Globalization;
using System.Xml.Linq;
using ZXing.Common;
using ZXing.OneD;

namespace Rewind.Core.Tests.Labels;

/// <summary>
/// A barcode as Code128.Svg drew it, read back from its markup: how wide it prints, how many modules
/// its viewBox spans, and each bar's place and width in modules. Nothing here renders the markup, so
/// the numbers in it stand in for a printout until one is scanned.
/// </summary>
internal sealed record DrawnBarcode(double WidthInches, int Modules, IReadOnlyList<(int X, int Width)> Bars)
{
    private const string Inches = "in";

    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>How wide one module prints, in inches.</summary>
    public double ModuleInches => WidthInches / Modules;

    public static DrawnBarcode Read(string svg)
    {
        var root = XElement.Parse(svg);

        // A width without "in" is in CSS pixels, which only a convention ties to the paper.
        var width = Value(root, "width");
        if (!width.EndsWith(Inches, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The svg is {width} wide, not a width in inches.");
        }

        var bars = root.Elements(SvgNamespace + "rect")
            .Select(rect => (Number(Value(rect, "x")), Number(Value(rect, "width"))))
            .ToList();
        return new DrawnBarcode(
            double.Parse(width[..^Inches.Length], CultureInfo.InvariantCulture),
            Number(Value(root, "viewBox").Split(' ')[2]),
            bars);
    }

    /// <summary>
    /// Reads the bars the way a scanner reads a line across them, one bit per module, with ZXing's own
    /// Code 128 reader.
    /// </summary>
    public string Decode()
    {
        var line = new BitArray(Modules);
        foreach (var (x, width) in Bars)
        {
            line.setRange(x, x + width);
        }

        return new Code128Reader().decodeRow(0, line, null)?.Text
            ?? throw new InvalidOperationException("ZXing's reader found no Code 128 barcode in the bars.");
    }

    private static string Value(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value
            ?? throw new InvalidOperationException($"The {element.Name.LocalName} has no {attribute}.");

    private static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}
