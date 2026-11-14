using System.Diagnostics;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Checkout;
using Rewind.Core.Concessions;
using Rewind.Core.Devices;
using Rewind.Core.LateFees;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class CheckoutTests : BunitContext
{
    private const string MemberCard = "MEM-001";
    private const string Label = "RV-00042";
    private const string BoxSetLabel = "RV-00043";
    private const string OtherBoxSetLabel = "RV-00044";
    private const string FactoryUpc = "032429258373";
    private const string CardFirst = "Scan the member's card first.";

    private static readonly DateTime DrawnAt = new(2026, 10, 24, 22, 15, 0, DateTimeKind.Utc);
    private static readonly FeeDue Fee = new("Late fee: Halloween (1978)", 100, "Halloween", 1978);
    private static readonly OpenedSale ClearSale = new("Dad", new LateFeeDrawRow(7, 3, 5, Card.Clear, null, DrawnAt), 200, null);
    private static readonly OpenedSale OverdueSale = new("Dad", new LateFeeDrawRow(7, 3, 5, Card.Overdue, 10, DrawnAt), 200, Fee);
    private static readonly MovieInCase Halloween = new(10, "Halloween", 1978);
    private static readonly MovieInCase Scream = new(11, "Scream", 1996);
    private static readonly MovieInCase Alien = new(12, "Alien", 1979);
    private static readonly CaseToRent OneMovie = new(1, Label, [Halloween]);
    private static readonly CaseToRent BoxSet = new(2, BoxSetLabel, [Halloween, Scream]);
    private static readonly CaseToRent OtherBoxSet = new(3, OtherBoxSetLabel, [Halloween, Scream, Alien]);
    private static readonly ConcessionRow Popcorn = new(1, "Popcorn", "CON-POPCORN", 100, IsActive: true);
    private static readonly ConcessionRow OldCandy = new(2, "Candy", "CON-CANDY", 50, IsActive: false);
    private static readonly SaleResult SavedSale = new(SaleStatus.Completed, 200, null, new CompletedSale(1, 200, 1000, 800, [(500, 1), (100, 3)], [300, 400, 500, 1000]));

    private readonly FakeCheckoutCounter _counter = new();
    private readonly FakeConcessionBar _bar = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly DialogService _dialogs;
    private readonly ListLogger<Checkout> _log = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private bool _crashed;

    public CheckoutTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        _dialogs = new DialogService(_sounds);
        _bar.Rows.AddRange([Popcorn, OldCandy]);
        _counter.Cases[Label] = OneMovie;
        _counter.Cases[BoxSetLabel] = BoxSet;
        _counter.Cases[OtherBoxSetLabel] = OtherBoxSet;
        Services.AddSingleton<ICheckoutCounter>(_counter);
        Services.AddSingleton<IConcessionBar>(_bar);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<ISoundPlayer>(_sounds);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<ILogger<Checkout>>(_log);
        Services.AddSingleton(errors);
    }

    [Fact]
    public void Render_ScreenOpens_SaysToScanACard()
    {
        var cut = Render<Checkout>();

        Assert.Equal("Scan a member card to start a rental.", Prompt(cut));
    }

    // UI-3: the Cash box has the focus, so Enter completes a sale.
    [Fact]
    public void Render_ScreenOpens_FocusesTheCashBox()
    {
        var cut = Render<Checkout>();

        AssertFocusedLast(cut.Find("#checkout-cash").GetAttribute("blazor:elementreference"), focuses: 1);
    }

    // SEC-5: the box takes no more than the longest amount Dollars reads. Dollars does the real checking.
    [Fact]
    public void Render_CashBox_CapsHowMuchFits()
    {
        var cut = Render<Checkout>();

        Assert.Equal("9", cut.Find("#checkout-cash").GetAttribute("maxlength"));
    }

    // The snack buttons are for a sale, so they aren't there before one.
    [Fact]
    public void Render_NoSale_ShowsNoSnackButtons()
    {
        var cut = Render<Checkout>();

        Assert.Empty(cut.FindAll(".checkout-snacks button"));
    }

    // The Overview's words, which the R2 wall sheet's example test asserts.
    [Fact]
    public async Task Scanned_FactoryUpcWhenIdle_AsksForTheLabel()
    {
        var cut = Render<Checkout>();

        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        AssertWarning("Please scan the Rewind Video label.");
    }

    [Fact]
    public async Task Scanned_FactoryUpcDuringASale_AsksForTheLabel()
    {
        var cut = await RenderWithSaleAsync();

        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        AssertWarning("Please scan the Rewind Video label.");
    }

    [Fact]
    public async Task Scanned_FactoryUpcAfterASale_AsksForTheLabel()
    {
        var cut = await RenderWithCompletedSaleAsync();

        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        AssertWarning("Please scan the Rewind Video label.");
    }

    // HW-5: ScannerHub logged it and played the chord, so the screen has nothing to add.
    [Fact]
    public async Task Scanned_UnknownBarcode_DoesNothing()
    {
        var cut = await RenderWithSaleAsync();

        await ScanAsync(cut, ScanKind.Unknown, "WHAT-IS-THIS");

        Assert.Null(_dialogs.Current);
        Assert.Empty(_log.Entries);
    }

    [Theory]
    [InlineData(ScanKind.StoreLabel, Label)]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    public async Task Scanned_BeforeACard_AsksForTheCard(ScanKind kind, string code)
    {
        var cut = Render<Checkout>();

        await ScanAsync(cut, kind, code);

        AssertWarning(CardFirst);
        Assert.Empty(_counter.Labels);
    }

    [Theory]
    [InlineData(ScanKind.StoreLabel, Label)]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    public async Task Scanned_AfterASale_AsksForTheNextCard(ScanKind kind, string code)
    {
        var cut = await RenderWithCompletedSaleAsync();

        await ScanAsync(cut, kind, code);

        AssertWarning(CardFirst);
    }

    [Fact]
    public async Task Submit_CashBeforeACard_AsksForTheCard()
    {
        var cut = Render<Checkout>();

        var pay = StartPayingAsync(cut, "10");

        AssertWarning(CardFirst);
        Assert.Empty(_counter.Tickets);
        await CloseDialogAsync(cut);
        await pay;
    }

    // The status bar only names a card's member (Build Plan 3.1), so it falls to this screen to say it's no good.
    [Fact]
    public async Task Scanned_CardNobodyHas_SaysSo()
    {
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        AssertWarning("Nobody has that card yet.\n\nTell the Store Manager.");
    }

    [Fact]
    public async Task Scanned_InactiveMembersCard_SaysSo()
    {
        _counter.Starting = Task.FromResult(new SaleStart(SaleStartStatus.MemberInactive, null));
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        AssertWarning("That member's card has been turned off.\n\nTell the Store Manager.");
    }

    [Fact]
    public async Task Scanned_Card_ShowsTheMemberAndWhenItsDue()
    {
        var cut = await RenderWithSaleAsync();

        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
        Assert.Equal("Scan each movie, add snacks, then type the cash and press Enter.", Prompt(cut));
    }

    [Fact]
    public async Task Scanned_ClearCard_ShowsNoAlert()
    {
        var cut = await RenderWithSaleAsync();

        Assert.Null(_dialogs.Current);
        Assert.Empty(cut.FindAll(".overdue"));
    }

    // An empty list would only be an empty box above the total.
    [Fact]
    public async Task Scanned_Card_ShowsNoBillUntilSomethingIsOnIt()
    {
        var cut = await RenderWithSaleAsync();

        Assert.Empty(cut.FindAll(".checkout-ticket"));
    }

    // #58: it opens with OVERDUE RENTAL, and says the movie's overdue rather than that they watched it, since
    // until the watch log it may be one they've never seen. The dialog plays the chord itself.
    [Fact]
    public async Task Scanned_OverdueCard_ShowsTheAlert()
    {
        _counter.Sales[MemberCard] = OverdueSale;
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        AssertWarning("OVERDUE RENTAL\n\nHalloween (1978) is overdue.\n\nLate fee: $1.00");
        Assert.Equal([Sound.Error], _sounds.Played);
    }

    [Fact]
    public async Task Scanned_OverdueCardNamingNoMovie_SaysARentalIsOverdue()
    {
        _counter.Sales[MemberCard] = OverdueSale with { Fee = new FeeDue("Late fee", 100, null, null) };
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        AssertWarning("OVERDUE RENTAL\n\nA rental is overdue.\n\nLate fee: $1.00");
    }

    // Drawn before the alert, so the screen behind it says whose card it was, not "Looking up the card...".
    [Fact]
    public async Task Scanned_OverdueCard_ShowsTheMemberBehindTheAlert()
    {
        _counter.Sales[MemberCard] = OverdueSale;
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
        Assert.Equal("Scan each movie, add snacks, then type the cash and press Enter.", Prompt(cut));
    }

    // The fee goes on the bill once the clerk has read the alert out, not behind it.
    [Fact]
    public async Task Scanned_OverdueCard_KeepsTheFeeOffTheBillUntilTheAlertCloses()
    {
        _counter.Sales[MemberCard] = OverdueSale;
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        Assert.Empty(cut.FindAll(".overdue"));
    }

    [Fact]
    public async Task CloseAlert_OverdueCard_PutsTheFlashingFeeOnTheBill()
    {
        _counter.Sales[MemberCard] = OverdueSale;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);

        await CloseDialogAsync(cut);

        Assert.Equal("Late fee: Halloween (1978) · $1.00", cut.Find(".overdue").TextContent);
        Assert.Equal("Total: $1.00", cut.Find(".checkout-total").TextContent);
    }

    // Each line of the bill reads as the clerk rang it up, with the fee last, whenever it was added.
    [Fact]
    public async Task Scanned_CaseAndSnack_ListsTheCaseThenTheSnackThenTheFee()
    {
        var cut = await RenderWithSaleAsync(OverdueSale);

        await ScanLabelAsync(cut);
        await ScanAsync(cut, ScanKind.Concession, Popcorn.Code);

        Assert.Equal(
            ["RV-00042 · Halloween (1978) · $2.00", "Popcorn · $1.00", "Late fee: Halloween (1978) · $1.00"],
            Bill(cut));
    }

    [Fact]
    public async Task Scanned_BoxSet_AsksWhichMovie()
    {
        var cut = await RenderWithSaleAsync();

        await ScanLabelAsync(cut, BoxSetLabel);

        Assert.Equal(["Halloween", "Scream"], cut.FindAll(".which-movie tbody tr").Select(row => row.Children[0].TextContent));
        Assert.Equal("Which movie is going home? Press Enter to pick it, or Esc to put the case back.", Prompt(cut));
        Assert.Empty(Bill(cut));
    }

    // Enter is the picker's while it's open, so the box and the buttons are out of the way until then.
    [Fact]
    public async Task Scanned_BoxSet_HidesTheCashBoxAndSnacks()
    {
        var cut = await RenderWithSaleAsync();

        await ScanLabelAsync(cut, BoxSetLabel);

        Assert.Empty(cut.FindAll("#checkout-cash"));
        Assert.Empty(cut.FindAll(".checkout-snacks button"));
    }

    [Fact]
    public async Task KeyDownEnter_InWhichMovie_AddsThePick()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);

        await cut.FindAll(".which-movie tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["RV-00043 · Scream (1996) · $2.00"], Bill(cut));
        Assert.Empty(cut.FindAll(".which-movie"));
    }

    // Esc there drops that case, and never reaches the sale's own Esc, which would ask to cancel it all.
    [Fact]
    public async Task KeyDownEscape_InWhichMovie_PutsTheCaseBackWithoutAsking()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        await ScanLabelAsync(cut, BoxSetLabel);

        var escape = cut.Find(".which-movie tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(_dialogs.Current);
        await escape;
        Assert.Empty(cut.FindAll(".which-movie"));
        Assert.Equal(["RV-00042 · Halloween (1978) · $2.00"], Bill(cut));
    }

    // The next box set gets its own list, starting at its own first row.
    [Fact]
    public async Task Scanned_SecondBoxSet_ListsItsOwnMovies()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);
        await cut.FindAll(".which-movie tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await ScanLabelAsync(cut, OtherBoxSetLabel);

        Assert.Equal(["Halloween", "Scream", "Alien"], cut.FindAll(".which-movie tbody tr").Select(row => row.Children[0].TextContent));
        Assert.Equal("Halloween", cut.Find(".which-movie tr.highlighted").Children[0].TextContent);
    }

    // UI-3: the picker takes the focus, and the Cash box gets it back, so Enter pays.
    [Fact]
    public async Task KeyDownEnter_InWhichMovie_GivesTheFocusBack()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);

        await cut.Find(".which-movie tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        AssertFocusedLast(cut.Find("#checkout-cash").GetAttribute("blazor:elementreference"), focuses: 4); // a new box: the picker replaced the old
    }

    [Fact]
    public async Task KeyDownEscape_InWhichMovie_GivesTheFocusBack()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);

        await cut.Find(".which-movie tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        AssertFocusedLast(cut.Find("#checkout-cash").GetAttribute("blazor:elementreference"), focuses: 4);
    }

    [Fact]
    public async Task Scanned_DuringWhichMovie_IsIgnoredAndLogged()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);

        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        Assert.Null(_dialogs.Current);
        AssertLoggedIgnored($"Ignored the Upc {FactoryUpc}: Checkout is still busy, a movie is being picked, or a dialog is open");
    }

    // A case goes home once, and the database would refuse a second line for it (DATA-11).
    [Fact]
    public async Task Scanned_CaseAlreadyOnTheBill_SaysSo()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 is already on this sale.");
        Assert.Equal([Label], _counter.Labels);
    }

    [Fact]
    public async Task Scanned_LabelNoCaseHas_SaysSo()
    {
        var cut = await RenderWithSaleAsync();

        await ScanLabelAsync(cut, "RV-99999");

        AssertWarning("No case has the label RV-99999.\n\nTell the Store Manager.");
    }

    [Fact]
    public async Task Scanned_CaseNotOnTheShelf_SaysSo()
    {
        var cut = await RenderWithSaleAsync();
        _counter.Scanning = Task.FromResult(new CaseScan(CaseScanStatus.NotOnShelf, null));

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 isn't on the shelf. It's checked out or retired.\n\nTell the Store Manager.");
        Assert.Empty(Bill(cut));
    }

    [Fact]
    public async Task Scanned_SnackCodeNobodyHas_SaysSo()
    {
        var cut = await RenderWithSaleAsync();

        await ScanAsync(cut, ScanKind.Concession, "CON-NACHOS");

        AssertWarning("No snack has the code CON-NACHOS.\n\nTell the Store Manager.");
    }

    // A menu printed before the snack was deactivated may still carry its barcode.
    [Fact]
    public async Task Scanned_SnackNoLongerSold_SaysSo()
    {
        var cut = await RenderWithSaleAsync();

        await ScanAsync(cut, ScanKind.Concession, OldCandy.Code);

        AssertWarning("Candy isn't on sale any more.");
        Assert.Empty(Bill(cut));
    }

    // A card mid-sale would otherwise drop the bill.
    [Fact]
    public async Task Scanned_CardDuringASale_SaysToFinishIt()
    {
        var cut = await RenderWithSaleAsync();

        await ScanCardAsync(cut);

        AssertWarning("Finish this sale, or press Esc to cancel it.");
        Assert.Equal([MemberCard], _counter.Cards);
    }

    // One thing at a time, as in Intake.
    [Fact]
    public async Task Scanned_WhileADialogIsOpen_IsIgnoredAndLogged()
    {
        var cut = await RenderWithSaleAsync();
        await ScanCardAsync(cut);

        await ScanLabelAsync(cut);

        Assert.Empty(_counter.Labels);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Checkout is still busy, a movie is being picked, or a dialog is open");
    }

    [Fact]
    public async Task Scanned_WhileCoreIsAnswering_IsIgnoredAndLogged()
    {
        _counter.Starting = new TaskCompletionSource<SaleStart>().Task;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);

        await ScanLabelAsync(cut);

        Assert.Equal("Looking up the card...", Prompt(cut));
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Checkout is still busy, a movie is being picked, or a dialog is open");
    }

    // Each snack on sale gets a button, with its price, and a snack no longer sold doesn't.
    [Fact]
    public async Task Scanned_Card_ShowsAButtonForEachSnackOnSale()
    {
        var cut = await RenderWithSaleAsync();

        Assert.Equal(["Popcorn $1.00"], cut.FindAll(".checkout-snacks button").Select(button => button.TextContent));
    }

    [Fact]
    public async Task Click_SnackButton_PutsItOnTheBill()
    {
        var cut = await RenderWithSaleAsync();

        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Popcorn · $1.00"], Bill(cut));
    }

    // UI-3: the button hands the focus back, so Enter pays rather than adding another popcorn.
    [Fact]
    public async Task Click_SnackButton_GivesTheFocusBack()
    {
        var cut = Render<Checkout>();
        var cashBox = cut.Find("#checkout-cash").GetAttribute("blazor:elementreference");
        _counter.Sales[MemberCard] = ClearSale;
        await ScanCardAsync(cut);

        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());

        AssertFocusedLast(cashBox, focuses: 3);
    }

    // Two of a snack are two lines, which the database holds apart: SQLite treats their NULL draws as distinct.
    [Fact]
    public async Task Click_SnackButtonTwice_ListsItTwice()
    {
        var cut = await RenderWithSaleAsync();

        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Popcorn · $1.00", "Popcorn · $1.00"], Bill(cut));
        Assert.Equal("Total: $2.00", cut.Find(".checkout-total").TextContent);
    }

    // Build Plan 3.3's Done when: a box set, popcorn and a late fee show the right total and change.
    [Fact]
    public async Task Submit_DoneWhenSale_ShowsTheTotalThenTheChangeAndHowToCountItBack()
    {
        var cut = await RenderWithSaleAsync(OverdueSale);
        await ScanLabelAsync(cut, BoxSetLabel);
        await cut.FindAll(".which-movie tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());
        Assert.Equal("Total: $4.00", cut.Find(".checkout-total").TextContent);

        await PayAsync(cut, "10");

        Assert.Equal("CHANGE $6.00", cut.Find(".checkout-change").TextContent);
        Assert.Equal("1 × $5, 1 × $1", cut.Find(".checkout-breakdown").TextContent);
        Assert.Equal("Count back from $4.00: $5.00, $10.00", cut.Find(".checkout-count-back").TextContent);
        Assert.Equal("Scan the next member's card.", Prompt(cut));
    }

    // Bills by the dollar, and coins by the cent.
    [Fact]
    public async Task Submit_ChangeWithCoins_NamesThem()
    {
        var cut = await RenderWithSaleAsync(ClearSale with { RentalPriceCents = 425 });
        await ScanLabelAsync(cut);

        await PayAsync(cut, "10");

        Assert.Equal("1 × $5, 3 × 25¢", cut.Find(".checkout-breakdown").TextContent);
        Assert.Equal("Count back from $4.25: $4.50, $4.75, $5.00, $10.00", cut.Find(".checkout-count-back").TextContent);
    }

    [Fact]
    public async Task Submit_ExactCash_HasNoChangeToCount()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        await PayAsync(cut, "2");

        Assert.Equal("CHANGE $0.00", cut.Find(".checkout-change").TextContent);
        Assert.Empty(cut.FindAll(".checkout-breakdown"));
        Assert.Empty(cut.FindAll(".checkout-count-back"));
    }

    // Nothing is left to flash, and nothing to pay: the bill goes with the sale.
    [Fact]
    public async Task Submit_Completed_ClearsTheBillAndTheCashBox()
    {
        var cut = await RenderWithSaleAsync(OverdueSale);

        await PayAsync(cut, "10");

        Assert.Empty(cut.FindAll(".checkout-ticket"));
        Assert.Empty(cut.FindAll(".overdue"));
        Assert.Empty(cut.FindAll("#checkout-cash"));
    }

    // The fee Start offered has to reach Complete: Core saves what the ticket says, and doesn't check.
    [Fact]
    public async Task Submit_OverdueSale_SendsTheFeeStartOffered()
    {
        var offered = Fee with { PriceCents = 135 };
        var cut = await RenderWithSaleAsync(OverdueSale with { Fee = offered });

        await PayAsync(cut, "10");

        var line = Assert.Single(Assert.Single(_counter.Tickets).Lines);
        Assert.Equal(new TicketLine(offered.LineName, 135, null, OverdueSale.Draw.Id), line);
    }

    // The ticket is the record of what the screen showed (DATA-10), so these are the prices on the bill.
    [Fact]
    public async Task Submit_Sale_SendsThePricesTheScreenShowed()
    {
        _bar.Rows[0] = Popcorn with { PriceCents = 125 };
        var cut = await RenderWithSaleAsync(ClearSale with { RentalPriceCents = 350 });
        await ScanLabelAsync(cut);
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());
        Assert.Equal(["RV-00042 · Halloween (1978) · $3.50", "Popcorn · $1.25"], Bill(cut));
        Assert.Equal("Total: $4.75", cut.Find(".checkout-total").TextContent);

        await PayAsync(cut, "5");

        var ticket = Assert.Single(_counter.Tickets);
        Assert.Equal(350, Assert.Single(ticket.Items).PriceCents);
        Assert.Equal(new TicketLine("Popcorn", 125, Popcorn.Id, null), Assert.Single(ticket.Lines));
        Assert.Equal(475, ticket.TotalCents);
    }

    // The member and the night come from the draw, and the pick is the one the clerk picked.
    [Fact]
    public async Task Submit_Sale_SendsWhoWhenAndWhat()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut, BoxSetLabel);
        await cut.FindAll(".which-movie tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await PayAsync(cut, "$2.50");

        var ticket = Assert.Single(_counter.Tickets);
        Assert.Equal((ClearSale.Draw.MemberId, ClearSale.Draw.BusinessDayId, 250), (ticket.MemberId, ticket.BusinessDayId, ticket.CashTenderedCents));
        Assert.Equal(new TicketItem(BoxSet.CaseId, BoxSetLabel, Scream.MovieId, Scream.Title, Scream.Year, 200), Assert.Single(ticket.Items));
    }

    [Fact]
    public async Task Submit_CashUnderTheTotal_NamesTheTotalAndCompletesNothing()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        var pay = StartPayingAsync(cut, "1.99");

        AssertWarning("That isn't enough. The total is $2.00.");
        await CloseDialogAsync(cut);
        await pay;
        Assert.Empty(cut.FindAll(".checkout-change"));
        Assert.DoesNotContain(Sound.Chime, _sounds.Played);
        Assert.Equal(["RV-00042 · Halloween (1978) · $2.00"], Bill(cut));
    }

    // What was typed stays in view behind the dialog, and the clerk types the money again after it.
    [Fact]
    public async Task CloseDialog_CashUnderTheTotal_EmptiesTheCashBox()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        var pay = StartPayingAsync(cut, "1.99");
        Assert.Equal("1.99", CashTyped(cut));

        await CloseDialogAsync(cut);
        await pay;

        Assert.Equal(string.Empty, CashTyped(cut));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two")]
    [InlineData("-1")]
    public async Task Submit_NotAnAmount_SaysHowToTypeItAndSendsNothing(string cash)
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        var pay = StartPayingAsync(cut, cash);

        AssertWarning("That isn't an amount of money.\n\nType it in dollars, such as 10 or 4.50.");
        Assert.Empty(_counter.Tickets);
        await CloseDialogAsync(cut);
        await pay;
    }

    // What was typed stays in view behind the dialog, and the clerk types the money again after it.
    [Fact]
    public async Task CloseDialog_NotAnAmount_EmptiesTheCashBox()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        var pay = StartPayingAsync(cut, "two");
        Assert.Equal("two", CashTyped(cut));

        await CloseDialogAsync(cut);
        await pay;

        Assert.Equal(string.Empty, CashTyped(cut));
    }

    [Fact]
    public async Task Submit_NothingOnTheBill_SaysSoAndSendsNothing()
    {
        var cut = await RenderWithSaleAsync();

        var pay = StartPayingAsync(cut, "10");

        AssertWarning("There's nothing on this sale yet.\n\nScan a movie or add a snack, or press Esc.");
        Assert.Empty(_counter.Tickets);
        await CloseDialogAsync(cut);
        await pay;
    }

    // The money wasn't what was wrong: it's still good once there's something to pay for.
    [Fact]
    public async Task CloseDialog_NothingOnTheBill_KeepsTheCash()
    {
        var cut = await RenderWithSaleAsync();
        var pay = StartPayingAsync(cut, "10");

        await CloseDialogAsync(cut);
        await pay;

        Assert.Equal("10", CashTyped(cut));
    }

    // A sale of nothing but snacks is a sale.
    [Fact]
    public async Task Submit_SnacksAlone_Completes()
    {
        var cut = await RenderWithSaleAsync();
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());

        await PayAsync(cut, "1");

        Assert.Equal("CHANGE $0.00", cut.Find(".checkout-change").TextContent);
    }

    // The case left the shelf between its scan and the cash. Nothing was saved, so the sale stays open.
    [Fact]
    public async Task Submit_CaseLeftTheShelf_TakesItOffTheBillAndKeepsTheSale()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());
        _counter.Completing = Task.FromResult(new SaleResult(SaleStatus.NotOnShelf, 300, Label, null));

        var pay = StartPayingAsync(cut, "10");

        AssertWarning("RV-00042 isn't on the shelf any more, so it's off this sale.\n\nCheck the total, then press Enter again.");
        await CloseDialogAsync(cut);
        await pay;
        Assert.Equal(["Popcorn · $1.00"], Bill(cut));
        Assert.Equal("Total: $1.00", cut.Find(".checkout-total").TextContent);
        Assert.DoesNotContain(Sound.Chime, _sounds.Played);
    }

    // The dialog says to press Enter again, which only works with the money still in the box.
    [Fact]
    public async Task CloseDialog_CaseLeftTheShelf_KeepsTheCash()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());
        _counter.Completing = Task.FromResult(new SaleResult(SaleStatus.NotOnShelf, 300, Label, null));
        var pay = StartPayingAsync(cut, "10");

        await CloseDialogAsync(cut);
        await pay;

        Assert.Equal("10", CashTyped(cut));
    }

    // The chime says the sale went through, so it waits for the save.
    [Fact]
    public async Task Submit_SaveStillGoing_DoesNotChimeYet()
    {
        var saved = new TaskCompletionSource<SaleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = saved.Task;

        var pay = StartPayingAsync(cut, "10");

        Assert.Equal("Saving the sale...", Prompt(cut));
        Assert.DoesNotContain(Sound.Chime, _sounds.Played);
        saved.SetResult(SavedSale);
        await pay;
        Assert.Contains(Sound.Chime, _sounds.Played);
    }

    // Two Enters would be two sales of one bill.
    [Fact]
    public async Task Submit_TwiceWhileSaving_SendsOneTicket()
    {
        var saved = new TaskCompletionSource<SaleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = saved.Task;
        var first = StartPayingAsync(cut, "10");

        var second = cut.Find(".checkout-cash").SubmitAsync();

        saved.SetResult(SavedSale);
        await first;
        await second;
        Assert.Single(_counter.Tickets);
    }

    // A save that failed saved nothing, so it mustn't sound as if it had. The screen's ErrorBoundary hears of it.
    [Fact]
    public async Task Submit_SaveFails_LeavesItToTheErrorBoundaryWithoutAChime()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = Task.FromException<SaleResult>(new InvalidOperationException("The save failed."));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => PayAsync(cut, "10"));

        Assert.Equal("The save failed.", thrown.Message);
        Assert.DoesNotContain(Sound.Chime, _sounds.Played);
    }

    // OBS-5: the screen checks for an empty bill before it asks, so Core saying so is a bug, not the clerk's mistake.
    [Fact]
    public async Task Submit_CoreSaysTheTicketIsEmpty_Throws()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = Task.FromResult(new SaleResult(SaleStatus.EmptyTicket, 0, null, null));

        await Assert.ThrowsAsync<UnreachableException>(() => PayAsync(cut, "10"));
    }

    // The ticket has gone, so a line added now would be on the screen and not on the sale.
    [Fact]
    public async Task Click_SnackButtonWhileSaving_AddsNothing()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = new TaskCompletionSource<SaleResult>().Task;
        _ = StartPayingAsync(cut, "10");

        await cut.Find(".checkout-snacks button").ClickAsync(new MouseEventArgs());

        Assert.Equal(["RV-00042 · Halloween (1978) · $2.00"], Bill(cut));
    }

    // No Cash box to hold the focus on the change, so the panel takes it, for Esc to reach the screen.
    [Fact]
    public async Task Submit_Completed_FocusesTheChange()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        await PayAsync(cut, "10");

        AssertFocusedLast(cut.Find(".checkout-done").GetAttribute("blazor:elementreference"), focuses: 3);
    }

    // A second checkout the same night, or the next member in line.
    [Fact]
    public async Task Scanned_CardAfterASale_StartsTheNextSale()
    {
        var cut = await RenderWithCompletedSaleAsync();

        await ScanCardAsync(cut);

        Assert.Empty(cut.FindAll(".checkout-change"));
        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
        Assert.Empty(Bill(cut));
        Assert.Equal(string.Empty, CashTyped(cut));
    }

    // The fee was the last sale's, and on this bill it would be charged a second time.
    [Fact]
    public async Task Scanned_CardAfterASaleWithAFee_LeavesTheFeeOffTheNextBill()
    {
        var cut = await RenderWithSaleAsync(OverdueSale);
        await PayAsync(cut, "10");
        _counter.Sales[MemberCard] = ClearSale;

        await ScanCardAsync(cut);

        Assert.Empty(Bill(cut));
    }

    // Cash typed before a card got its own dialog, so it's not this sale's.
    [Fact]
    public async Task Scanned_CardWithCashAlreadyTyped_EmptiesTheCashBox()
    {
        _counter.Sales[MemberCard] = ClearSale;
        var cut = Render<Checkout>();
        await cut.Find("#checkout-cash").InputAsync("10");

        await ScanCardAsync(cut);

        Assert.Equal(string.Empty, CashTyped(cut));
    }

    [Fact]
    public async Task Scanned_CardAfterASale_FocusesTheCashBox()
    {
        var cut = await RenderWithCompletedSaleAsync();

        await ScanCardAsync(cut);

        AssertFocusedLast(cut.Find("#checkout-cash").GetAttribute("blazor:elementreference"), focuses: 4);
    }

    // The new Cash box couldn't take the focus behind the alert, and the dialog has nothing to give back to.
    [Fact]
    public async Task CloseAlert_NextSale_FocusesTheCashBox()
    {
        var cut = await RenderWithCompletedSaleAsync();
        _counter.Sales[MemberCard] = OverdueSale;
        await ScanCardAsync(cut);
        var cashBox = cut.Find("#checkout-cash").GetAttribute("blazor:elementreference");

        await CloseDialogAsync(cut);

        AssertFocusedLast(cashBox, focuses: 5);
    }

    [Fact]
    public async Task KeyDownEscape_OnTheChange_GoesBackToIdle()
    {
        var cut = await RenderWithCompletedSaleAsync();

        await cut.Find(".checkout-done").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".checkout-change"));
        Assert.Equal("Scan a member card to start a rental.", Prompt(cut));
        Assert.Null(_dialogs.Current);
    }

    // The new box starts empty: what was typed for the last sale isn't cash for the next.
    [Fact]
    public async Task KeyDownEscape_OnTheChange_LeavesTheCashBoxEmptyWithTheFocus()
    {
        var cut = await RenderWithCompletedSaleAsync();

        await cut.Find(".checkout-done").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(string.Empty, CashTyped(cut));
        AssertFocusedLast(cut.Find("#checkout-cash").GetAttribute("blazor:elementreference"), focuses: 4);
    }

    // Info, with Cancel: the clerk pressed Esc on purpose, so it plays no chord.
    [Fact]
    public async Task KeyDownEscape_SaleWithSomethingOnIt_AsksFirst()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);

        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        var dialog = Assert.IsType<Dialog>(_dialogs.Current);
        Assert.Equal(("Cancel this sale?\n\nNothing from it has been saved.", DialogIcon.Info, true), (dialog.Message, dialog.Icon, dialog.CanCancel));
        Assert.Empty(_sounds.Played);
        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await escape;
    }

    [Fact]
    public async Task KeyDownEscape_AnsweredCancel_KeepsTheSale()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await escape;

        Assert.Equal(["RV-00042 · Halloween (1978) · $2.00"], Bill(cut));
    }

    [Fact]
    public async Task KeyDownEscape_AnsweredOk_DropsTheSale()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await escape;

        Assert.Empty(cut.FindAll(".checkout-member"));
        Assert.Equal("Scan a member card to start a rental.", Prompt(cut));
        Assert.Empty(_counter.Tickets);
    }

    // The cash typed for a sale goes with it, so it can't pay for the next.
    [Fact]
    public async Task KeyDownEscape_AnsweredOkWithCashTyped_EmptiesTheCashBox()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        await cut.Find("#checkout-cash").InputAsync("10");
        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await escape;

        Assert.Equal(string.Empty, CashTyped(cut));
    }

    // Whatever had the focus, such as a snack button, may have gone with the sale.
    [Fact]
    public async Task KeyDownEscape_SaleDropped_FocusesTheCashBox()
    {
        var cut = Render<Checkout>();
        var cashBox = cut.Find("#checkout-cash").GetAttribute("blazor:elementreference");
        _counter.Sales[MemberCard] = ClearSale;
        await ScanCardAsync(cut);
        await ScanLabelAsync(cut);
        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await escape;

        AssertFocusedLast(cashBox, focuses: 3);
    }

    // A fee on the bill counts: dropping it brings the alert back at the next scan (ADR-0010).
    [Fact]
    public async Task KeyDownEscape_SaleWithOnlyAFee_AsksFirst()
    {
        var cut = await RenderWithSaleAsync(OverdueSale);

        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.NotNull(_dialogs.Current);
        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await escape;
    }

    [Fact]
    public async Task KeyDownEscape_SaleWithNothingOnIt_DropsItWithoutAsking()
    {
        var cut = await RenderWithSaleAsync();

        var escape = cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(_dialogs.Current);
        Assert.Empty(cut.FindAll(".checkout-member"));
        await escape;
    }

    // The keys typed into the Cash box come through here too, and only Esc means anything.
    [Fact]
    public async Task KeyDown_AnyOtherKey_KeepsTheSale()
    {
        var cut = await RenderWithSaleAsync();

        await cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "1" });

        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
    }

    // Its answer would land on a screen that had moved on.
    [Fact]
    public async Task KeyDownEscape_WhileCoreIsAnswering_DoesNothing()
    {
        var cut = await RenderWithSaleAsync();
        _counter.Scanning = new TaskCompletionSource<CaseScan>().Task;
        await ScanLabelAsync(cut);

        await cut.Find("#checkout-cash").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
    }

    // The screen listens before it reads the snacks, so a card scanned as it opens still counts.
    [Fact]
    public async Task Scanned_WhileReadingTheSnacks_StartsTheSale()
    {
        _bar.Listing = new TaskCompletionSource<IReadOnlyList<ConcessionRow>>().Task;
        _counter.Sales[MemberCard] = ClearSale;
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        Assert.Equal("Dad · Due back tonight", cut.Find(".checkout-member").TextContent);
    }

    // The scanner outlives the screen, so the screen stops listening when the clerk leaves it.
    [Fact]
    public async Task Dispose_ClerkLeavesTheScreen_StopsListeningForScans()
    {
        _ = Render<Checkout>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A scan the scanner's thread raised just before the clerk left reaches the screen afterwards. The screen
    // has closed, so nothing is asked, and nothing is raised.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        _ = Render<Checkout>();
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.MemberCard, MemberCard));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.Empty(_counter.Cards);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    [Fact]
    public async Task Dispose_WhileReadingTheSnacks_CancelsTheRead()
    {
        _bar.Listing = new TaskCompletionSource<IReadOnlyList<ConcessionRow>>().Task;
        _ = Render<Checkout>();

        await DisposeComponentsAsync();

        Assert.True(_bar.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileLookingUpACard_CancelsTheLookup()
    {
        _counter.Starting = new TaskCompletionSource<SaleStart>().Task;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);

        await DisposeComponentsAsync();

        Assert.True(_counter.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileLookingUpACase_CancelsTheLookup()
    {
        var cut = await RenderWithSaleAsync();
        _counter.Scanning = new TaskCompletionSource<CaseScan>().Task;
        await ScanLabelAsync(cut);

        await DisposeComponentsAsync();

        Assert.True(_counter.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileLookingUpASnack_CancelsTheLookup()
    {
        var cut = await RenderWithSaleAsync();
        _bar.Finding = new TaskCompletionSource<ConcessionRow?>().Task;
        await ScanAsync(cut, ScanKind.Concession, Popcorn.Code);

        await DisposeComponentsAsync();

        Assert.True(_bar.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileSaving_CancelsTheSave()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = new TaskCompletionSource<SaleResult>().Task;
        _ = StartPayingAsync(cut, "10");

        await DisposeComponentsAsync();

        Assert.True(_counter.LastToken.IsCancellationRequested);
    }

    // A save whose commit beat the clerk leaving has gone through (NET-7), and the chime still says so.
    [Fact]
    public async Task Dispose_SaveFinishesAnyway_StillChimes()
    {
        var saved = new TaskCompletionSource<SaleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = saved.Task;
        var pay = StartPayingAsync(cut, "10");
        await DisposeComponentsAsync();

        saved.SetResult(SavedSale);
        await pay;

        Assert.Contains(Sound.Chime, _sounds.Played);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // The clerk left, so the sale went with them and nothing shows for it: this was the save's cancel, not an error.
    [Fact]
    public async Task Dispose_WhileSaving_RaisesNothingOnceCancelled()
    {
        var saved = new TaskCompletionSource<SaleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        _counter.Completing = saved.Task;
        var pay = StartPayingAsync(cut, "10");
        await DisposeComponentsAsync();

        saved.SetCanceled(_counter.LastToken); // as EF Core ends a save whose token was cancelled
        await pay;

        Assert.DoesNotContain(Sound.Chime, _sounds.Played);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.Null(_dialogs.Current);
    }

    // The cancellation leaving causes goes nowhere: WhileBusyAsync catches it, and ScanningScreen drops any
    // that gets past it, rather than sending it to ErrorHub as an error from a closed screen.
    [Fact]
    public async Task Dispose_WhileLookingUpACard_RaisesNothingOnceCancelled()
    {
        var answer = new TaskCompletionSource<SaleStart>();
        _counter.Starting = answer.Task;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);
        await DisposeComponentsAsync();

        answer.SetCanceled(_counter.LastToken); // as EF Core ends a read whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
        Assert.Null(_dialogs.Current);
    }

    // The dialogs outlive the screen, so a snack's cancelled lookup mustn't read as a code nobody has.
    [Fact]
    public async Task Dispose_WhileLookingUpASnack_ShowsNoDialog()
    {
        var answer = new TaskCompletionSource<ConcessionRow?>();
        var cut = await RenderWithSaleAsync();
        _bar.Finding = answer.Task;
        await ScanAsync(cut, ScanKind.Concession, Popcorn.Code);
        await DisposeComponentsAsync();

        answer.SetCanceled(_bar.LastToken);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Null(_dialogs.Current);
        Assert.False(_crashed);
    }

    // #35: a lookup that fails after the clerk left has no screen to show it on, so it goes to ErrorHub.
    [Fact]
    public async Task Dispose_WhileLookingUpACard_ReportsAFailureToErrorHub()
    {
        var answer = new TaskCompletionSource<SaleStart>();
        _counter.Starting = answer.Task;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);
        await DisposeComponentsAsync();

        answer.SetException(new InvalidOperationException("The read failed."));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.True(_crashed);
        Assert.Equal(["Unhandled exception from Checkout; reported to the shell"], _errorLog.Entries.Select(entry => entry.Message));
    }

    // While the screen is open, a failure goes to its ErrorBoundary, not to ErrorHub.
    [Fact]
    public async Task Scanned_StartFails_HandsItToTheErrorBoundary()
    {
        _counter.Starting = Task.FromException<SaleStart>(new InvalidOperationException("The read failed."));
        var cut = Render<Checkout>();

        await ScanCardAsync(cut);

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // ADR-0011: a card's lookup that answers after the clerk left, rather than being cancelled, would open
    // the OVERDUE RENTAL alert over the next screen. The dialogs outlive the screen, so it shows nothing instead.
    [Fact]
    public async Task Dispose_CardAnswersAfterTheClerkLeft_ShowsNoDialog()
    {
        var answer = new TaskCompletionSource<SaleStart>();
        _counter.Starting = answer.Task;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);
        await DisposeComponentsAsync();

        answer.SetResult(new SaleStart(SaleStartStatus.Started, OverdueSale)); // the draw, saved as the clerk left
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Null(_dialogs.Current);
        Assert.False(_crashed);
    }

    // A scan, as the scanner's thread raises it, then a turn of the screen's own thread, so the screen has
    // handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<Checkout> cut, ScanKind kind, string code)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    private Task ScanCardAsync(IRenderedComponent<Checkout> cut) => ScanAsync(cut, ScanKind.MemberCard, MemberCard);

    private Task ScanLabelAsync(IRenderedComponent<Checkout> cut, string label = Label) => ScanAsync(cut, ScanKind.StoreLabel, label);

    // The screen with a sale open, and an Overdue card's alert closed.
    private async Task<IRenderedComponent<Checkout>> RenderWithSaleAsync(OpenedSale? sale = null)
    {
        _counter.Sales[MemberCard] = sale ?? ClearSale;
        var cut = Render<Checkout>();
        await ScanCardAsync(cut);
        if (sale?.Fee is not null)
        {
            await CloseDialogAsync(cut);
        }

        return cut;
    }

    // The screen showing the change from a one-case sale.
    private async Task<IRenderedComponent<Checkout>> RenderWithCompletedSaleAsync()
    {
        var cut = await RenderWithSaleAsync();
        await ScanLabelAsync(cut);
        await PayAsync(cut, "10");
        return cut;
    }

    // Types the cash and presses Enter, for a sale that completes at once. A dialog would wait for someone to
    // close it, and the test with it, so a dialog fails the test instead.
    private async Task PayAsync(IRenderedComponent<Checkout> cut, string cash)
    {
        var submit = StartPayingAsync(cut, cash);
        Assert.Null(_dialogs.Current?.Message);
        await submit;
    }

    // Types the cash and presses Enter, and leaves the screen answering: with a dialog up, or waiting on the save.
    private static async Task StartPayingAsync(IRenderedComponent<Checkout> cut, string cash)
    {
        await cut.Find("#checkout-cash").InputAsync(cash);
        await cut.Find(".checkout-cash").SubmitAsync();
    }

    private async Task CloseDialogAsync(IRenderedComponent<Checkout> cut)
    {
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await cut.InvokeAsync(() => { }); // queued behind the screen's own continuation
    }

    private static string Prompt(IRenderedComponent<Checkout> cut) => cut.Find(".checkout-prompt").TextContent;

    private static string? CashTyped(IRenderedComponent<Checkout> cut) => cut.Find("#checkout-cash").GetAttribute("value");

    private static List<string> Bill(IRenderedComponent<Checkout> cut) =>
        [.. cut.FindAll(".checkout-ticket li").Select(line => line.TextContent)];

    private void AssertWarning(string message)
    {
        var dialog = Assert.IsType<Dialog>(_dialogs.Current);
        Assert.Equal((message, DialogIcon.Warning), (dialog.Message, dialog.Icon));
    }

    private void AssertLoggedIgnored(string message) => Assert.Contains((LogLevel.Debug, message, (Exception?)null), _log.Entries);

    // UI-3: the element the screen focused last, and how many focus calls it has made so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }
}
