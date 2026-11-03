using Bunit;
using Rewind.Core.Labels;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class PrintableMenuTests : BunitContext
{
    // Build Plan 3.1: each snack's name, price and code, in the order Core gives them.
    [Fact]
    public void Render_Menu_ListsEachSnackWithItsPriceAndCode()
    {
        var cut = RenderMenu(FakeLabelSheets.SnackMenu);

        Assert.Equal(
            [["Candy", "$0.50", "CON-CANDY"], ["Popcorn", "$1.00", "CON-POPCORN"], ["Soda", "$0.75", "CON-SODA"]],
            Lines(cut));
    }

    // Each line carries Core's own barcode, at the menu's size (#21).
    [Fact]
    public void Render_Menu_DrawsEachSnacksBarcode()
    {
        var cut = RenderMenu(FakeLabelSheets.SnackMenu);

        var barcodes = cut.FindAll(".menu-line svg");
        Assert.Equal(3, barcodes.Count);
        Assert.All(barcodes, svg => Assert.Equal(("3.5in", "0.5in"), (svg.GetAttribute("width"), svg.GetAttribute("height"))));
    }

    // The menu is the store's whole price list. Its heading is the table's head, which a printed table
    // repeats on each sheet.
    [Fact]
    public void Render_Menu_HeadsEachSheetWithTheStoreAndItsPrices()
    {
        var cut = RenderMenu(FakeLabelSheets.SnackMenu);

        Assert.Equal(
            ("Rewind Video", "Movie rentals $2.00 each · Late fee $1.00"),
            (cut.Find("thead .menu-store-name").TextContent, cut.Find("thead .menu-store-prices").TextContent));
    }

    // The date is how to tell a stale menu from the counter's, so it's the table's foot, which each sheet
    // repeats too.
    [Fact]
    public void Render_Menu_DatesEachSheetInTheFoot()
    {
        var cut = RenderMenu(FakeLabelSheets.SnackMenu);

        Assert.Equal("Printed October 23, 2026", cut.Find("tfoot .menu-date").TextContent);
    }

    // The Store Manager shows it only while there's a menu.
    [Fact]
    public void Render_NoMenu_DrawsNothing()
    {
        var cut = RenderMenu(null);

        Assert.Equal("", cut.Markup.Trim());
    }

    private IRenderedComponent<PrintableMenu> RenderMenu(ConcessionMenu? menu) =>
        Render<PrintableMenu>(parameters => parameters.Add(p => p.Menu, menu));

    // Each line's name, price and code, top to bottom.
    private static List<string[]> Lines(IRenderedComponent<PrintableMenu> cut) =>
        [.. cut.FindAll(".menu-line").Select(line => new[]
        {
            line.QuerySelector(".menu-name")?.TextContent ?? "",
            line.QuerySelector(".menu-price")?.TextContent ?? "",
            line.QuerySelector(".menu-code")?.TextContent ?? "",
        })];
}
