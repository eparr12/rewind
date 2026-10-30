using System.Globalization;
using System.Xml.Linq;
using ZXing.OneD;

namespace Rewind.Core.Labels;

/// <summary>
/// Draws Rewind's barcodes in Code 128 as SVG, for printed sheets of store labels, such as RV-00042,
/// and member cards, such as MEM-001 (Overview: Barcodes and intake), and for the concession menu's
/// snacks, such as CON-POPCORN (Build Plan 3.1). ZXing.Net works out the bars,
/// and this draws them one unit per module, stretched to the printed size, so a module prints exactly
/// the printed width divided by the number of modules, whatever the code.
/// </summary>
public static class Code128
{
    /// <summary>
    /// The white drawn either side of the bars, in modules: 10, the least Code 128 allows. It's part of
    /// the barcode's own width, so the barcode keeps it wherever a sheet puts it.
    /// </summary>
    public const int QuietZoneModules = 10;

    /// <summary>
    /// The narrowest a module may print, in inches: 10 mil, a hundredth of an inch, the narrowest bar a
    /// scanner reads reliably. A code too long for the width it's given is refused, not printed narrower.
    /// </summary>
    public const double LeastModuleInches = 0.010;

    /// <summary>
    /// How wide a store label's barcode prints, in inches, quiet zones included, on a label 2.625 inches
    /// wide. A store label is 112 modules, 132 with its quiet zones, so a module is 1/60 inch: wider than
    /// <see cref="LeastModuleInches"/>, and a whole 5 or 10 dots on a 300 or 600 dpi printer. With the
    /// barcode's left edge on a whole dot too, which is the sheet's layout to keep, every module prints
    /// the same number of dots.
    /// </summary>
    public const double StoreLabelWidthInches = 2.2;

    /// <summary>How tall a store label's barcode prints, in inches, on a label 1 inch tall.</summary>
    public const double StoreLabelHeightInches = 0.45;

    /// <summary>
    /// How wide a member card's barcode prints, in inches, quiet zones included, on a card 3.5 inches
    /// wide. A member card is 112 modules too, so a module is 1/50 inch, a whole 6 or 12 dots.
    /// </summary>
    public const double MemberCardWidthInches = 2.64;

    /// <summary>How tall a member card's barcode prints, in inches, on a card 2 inches tall.</summary>
    public const double MemberCardHeightInches = 0.5;

    /// <summary>
    /// How wide a snack's barcode prints on the concession menu, in inches, quiet zones included (#21). A
    /// concession code is up to 24 characters, CON- and 20 letters, which make 319 modules with their quiet
    /// zones, so a module prints at least 3.5/319 inch, about 11 mil. A member card's width holds codes of
    /// up to 19 characters.
    /// </summary>
    public const double MenuWidthInches = 3.5;

    /// <summary>How tall a snack's barcode prints on the concession menu, in inches: a member card's height.</summary>
    public const double MenuHeightInches = 0.5;

    // The drawing is one unit tall, and every bar is as tall as the drawing, so the svg's stretch to the
    // printed height reaches every bar.
    private const int ViewBoxHeight = 1;

    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Draws a code as one svg element, sized in inches. The element holds only numbers, never the
    /// code's text, so a page can take it as markup.
    /// </summary>
    /// <param name="code">What the barcode holds, such as RV-00042.</param>
    /// <param name="widthInches">
    /// How wide it prints, quiet zones included: wide enough for each module to print at least
    /// <see cref="LeastModuleInches"/>.
    /// </param>
    /// <param name="heightInches">How tall it prints.</param>
    /// <exception cref="ArgumentNullException">The code is null.</exception>
    /// <exception cref="ArgumentException">The code is empty, or has a character Code 128 can't hold, such as é.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The height isn't above zero, or the width is too narrow for the code, so its modules would print
    /// narrower than <see cref="LeastModuleInches"/>, or isn't a number.
    /// </exception>
    public static string Svg(string code, double widthInches, double heightInches)
    {
        // ZXing.Net throws a NullReferenceException for null, and draws an empty code as a checksum and a
        // stop with no start, which no scanner reads.
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightInches);

        // ZXing.Net's own SVG writer isn't used: its output starts with an XML declaration, a comment
        // and a DOCTYPE, which don't belong inside a page, and has no printed size. Its encoder gives
        // the code's modules alone, dark or light.
        var modules = new Code128Writer().encode(code);
        var modulesWide = QuietZoneModules + modules.Length + QuietZoneModules;
        ThrowIfModulesTooNarrow(widthInches, modulesWide);

        // One unit per module, stretched to the printed size: preserveAspectRatio="none" lets the
        // one-unit-tall bars fill the height while their widths stay exact. crispEdges only matters on
        // screen, where it keeps bars a pixel or two wide sharp rather than gray; on paper, WebView2
        // prints the rects as shapes, and the printer draws their edges itself. A fill set on the svg
        // wins over one it would inherit from the page around it.
        var svg = new XElement(
            SvgNamespace + "svg",
            new XAttribute("viewBox", string.Create(CultureInfo.InvariantCulture, $"0 0 {modulesWide} {ViewBoxHeight}")),
            new XAttribute("width", Inches(widthInches)),
            new XAttribute("height", Inches(heightInches)),
            new XAttribute("preserveAspectRatio", "none"),
            new XAttribute("shape-rendering", "crispEdges"),
            new XAttribute("fill", "black"),
            Bars(modules).Select(bar => new XElement(
                SvgNamespace + "rect",
                new XAttribute("x", QuietZoneModules + bar.Start),
                new XAttribute("width", bar.Width),
                new XAttribute("height", ViewBoxHeight))));
        return svg.ToString(SaveOptions.DisableFormatting);
    }

    // Every module is stretched to fit the width, so a longer code prints narrower bars. Bars too narrow
    // to scan would be a sheet laid out wrong, not a slip of the clerk's (NET-12), so they're refused
    // here instead of printing as labels that never scan. So is a width of zero or less, and one that
    // isn't a number, which no comparison is less than.
    private static void ThrowIfModulesTooNarrow(double widthInches, int modulesWide)
    {
        if (double.IsNaN(widthInches) || widthInches / modulesWide < LeastModuleInches)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthInches),
                widthInches,
                string.Create(CultureInfo.InvariantCulture, $"At {widthInches} inches wide, {modulesWide} modules would each print narrower than {LeastModuleInches} inch."));
        }
    }

    // Each run of dark modules is one bar: where it starts and how many modules wide it is. The light
    // runs between the bars are the paper.
    private static IEnumerable<(int Start, int Width)> Bars(bool[] modules)
    {
        var start = 0;
        while (start < modules.Length)
        {
            var end = start + 1;
            while (end < modules.Length && modules[end] == modules[start])
            {
                end++;
            }

            if (modules[start])
            {
                yield return (start, end - start);
            }

            start = end;
        }
    }

    private static string Inches(double inches) => string.Create(CultureInfo.InvariantCulture, $"{inches}in");
}
