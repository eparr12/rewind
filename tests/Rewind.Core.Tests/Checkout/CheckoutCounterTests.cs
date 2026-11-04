using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.MovieNights;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.Checkout;

public sealed class CheckoutCounterTests
{
    private const string MemberCard = "MEM-001";
    private const string Label = "RV-00042";
    private const string OtherLabel = "RV-00043";

    // A bag with one card left, so the first scan's draw is the card a test wants.
    private const string OverdueBag = "O";
    private const string ClearBag = "C";

    private static readonly Movie[] Halloween = [new Movie { Title = "Halloween", Year = 1978 }];

    [Fact]
    public async Task StartAsync_CardNobodyHas_IsNoSuchMemberAndDrawsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var start = await Counter(rewind).StartAsync(MemberCard, ct);

        Assert.Equal(new SaleStart(SaleStartStatus.NoSuchMember, null), start);
        await AssertNothingDrawnAsync(rewind, ct);
    }

    // DrawAsync doesn't check that a member is active, so a deactivated card would draw from their bag.
    [Fact]
    public async Task StartAsync_InactiveMember_IsMemberInactiveAndDrawsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct, isActive: false);

        var start = await Counter(rewind).StartAsync(MemberCard, ct);

        Assert.Equal(new SaleStart(SaleStartStatus.MemberInactive, null), start);
        await AssertNothingDrawnAsync(rewind, ct);
    }

    // The screen prices each case at this, as the clerk sees it.
    [Fact]
    public async Task StartAsync_ActiveMember_GivesTheirNameTheirDrawAndTheRentalPriceNow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, ClearBag, ct);
        await SavePricesAsync(rewind, "3.50", "1.00", ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Equal(("Dad", member, Card.Clear, 350), (sale.MemberName, sale.Draw.MemberId, sale.Draw.Card, sale.RentalPriceCents));
    }

    [Fact]
    public async Task StartAsync_ClearCard_GivesNoFee()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Null(sale.Fee);
    }

    [Fact]
    public async Task StartAsync_OverdueCard_GivesTheFeeAtTheLateFeeNow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await SavePricesAsync(rewind, "2.00", "1.25", ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Equal(125, sale.Fee?.PriceCents);
    }

    [Fact]
    public async Task StartAsync_OverdueCard_NamesTheFlavorMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Equal(new FeeDue("Late fee: Halloween (1978)", 100, "Halloween", 1978), sale.Fee);
    }

    [Fact]
    public async Task StartAsync_FlavorMovieWithNoYear_NamesItsTitleAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await AddCaseAsync(rewind, Label, [new Movie { Title = "Home movies" }], ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Equal(new FeeDue("Late fee: Home movies", 100, "Home movies", null), sale.Fee);
    }

    // The collection can be empty, so an Overdue card doesn't have to name a movie.
    [Fact]
    public async Task StartAsync_OverdueCardWithNoMovieToName_IsALateFeeAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);

        var sale = await StartedAsync(rewind, ct);

        Assert.Equal(new FeeDue("Late fee", 100, null, null), sale.Fee);
    }

    // ADR-0010: a rescan gets the same draw back, and its fee is paid once.
    [Fact]
    public async Task StartAsync_AfterASaleThatPaidTheFee_GivesNoFee()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        var first = await StartedAsync(rewind, ct);
        await Counter(rewind).CompleteAsync(TicketFor(first, [], [FeeLine(first)], 100), ct);

        var second = await StartedAsync(rewind, ct);

        Assert.Equal(first.Draw, second.Draw);
        Assert.Null(second.Fee);
    }

    // ADR-0010: a fee line pays for its own draw and no one else's.
    [Fact]
    public async Task StartAsync_AfterAnotherMemberPaidTheirFee_StillGivesTheFee()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        var paid = await StartedAsync(rewind, ct);
        await Counter(rewind).CompleteAsync(TicketFor(paid, [], [FeeLine(paid)], 100), ct);
        await AddMemberAsync(rewind, OverdueBag, ct, card: "MEM-002");

        var start = await Counter(rewind).StartAsync("MEM-002", ct);

        Assert.Equal(100, start.Sale?.Fee?.PriceCents);
    }

    // ADR-0010: nothing was saved, so nothing was paid.
    [Fact]
    public async Task StartAsync_AfterASaleThatNeverCompleted_GivesTheFeeAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        var first = await StartedAsync(rewind, ct);

        var second = await StartedAsync(rewind, ct);

        Assert.Equal(first.Draw, second.Draw);
        Assert.Equal(first.Fee, second.Fee);
    }

    // ADR-0010: the fee on the bill is the late fee as it is by then.
    [Fact]
    public async Task StartAsync_LateFeeChangedSinceASaleThatNeverCompleted_GivesTheNewFee()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await StartedAsync(rewind, ct);
        await SavePricesAsync(rewind, "2.00", "1.50", ct);

        var second = await StartedAsync(rewind, ct);

        Assert.Equal(150, second.Fee?.PriceCents);
    }

    // NET-7: the draw has committed, so a clerk leaving just then doesn't turn it into an exception.
    [Fact]
    public async Task StartAsync_CancelledOnceDrawn_StillAnswers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        using var leaving = new CancellationTokenSource();
        var counter = Counter(rewind, draws: new LeavesOnceDrawn(Draws(rewind), leaving));

        var start = await counter.StartAsync(MemberCard, leaving.Token);

        Assert.Equal(SaleStartStatus.Started, start.Status);
        Assert.Equal(100, start.Sale?.Fee?.PriceCents);
    }

    // NET-7: a screen that closes doesn't draw on its way out.
    [Fact]
    public async Task StartAsync_Cancelled_DrawsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var start = () => Counter(rewind).StartAsync(MemberCard, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(start);
        await AssertNothingDrawnAsync(rewind, ct);
    }

    // NET-7: the token goes as far as the draw, whose own tests show it stops there. The test above can't show
    // this: a token that's already cancelled stops at the roster.
    [Fact]
    public async Task StartAsync_AnyCard_HandsTheTokenToTheDraw()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        using var leaving = new CancellationTokenSource();
        var draws = new HearsTheToken(Draws(rewind));

        await Counter(rewind, draws: draws).StartAsync(MemberCard, leaving.Token);

        Assert.Equal(leaving.Token, draws.Token);
    }

    [Fact]
    public async Task StartAsync_NullCard_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // NET-1: null on purpose, past the compiler's check, since this tests what StartAsync does with one.
        var start = () => Counter(rewind).StartAsync(null!, ct);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(start);
        Assert.Equal("cardBarcode", refusal.ParamName);
    }

    [Fact]
    public async Task ScanCaseAsync_OneMovieCase_GivesTheCaseAndItsMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var (caseId, movies) = await AddCaseAsync(rewind, Label, Halloween, ct);

        var found = await FoundAsync(rewind, Label, ct);

        Assert.Equal((caseId, Label), (found.CaseId, found.StoreLabel));
        Assert.Equal([new MovieInCase(movies[0], "Halloween", 1978)], found.Movies);
    }

    // The screen asks Which movie? for more than one, and a remake shares its title.
    [Fact]
    public async Task ScanCaseAsync_BoxSet_ListsEveryMovieByTitleThenYear()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        Movie[] boxSet =
        [
            new Movie { Title = "Halloween", Year = 2018 },
            new Movie { Title = "alien", Year = 1979 },
            new Movie { Title = "Halloween", Year = 1978 },
        ];
        await AddCaseAsync(rewind, Label, boxSet, ct);

        var found = await FoundAsync(rewind, Label, ct);

        Assert.Equal(["alien (1979)", "Halloween (1978)", "Halloween (2018)"], found.Movies.Select(m => Movie.TitleWithYear(m.Title, m.Year)));
    }

    [Fact]
    public async Task ScanCaseAsync_LabelInLowerCase_IsReadAsAScanIs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);

        var found = await FoundAsync(rewind, "rv-00042", ct);

        Assert.Equal(Label, found.StoreLabel);
    }

    [Fact]
    public async Task ScanCaseAsync_LabelNoCaseHas_IsNoSuchCase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var scan = await Counter(rewind).ScanCaseAsync(Label, ct);

        Assert.Equal(new CaseScan(CaseScanStatus.NoSuchCase, null), scan);
    }

    // Nothing but a store label is worth asking the database about.
    [Fact]
    public async Task ScanCaseAsync_NotAStoreLabel_IsNoSuchCaseWithoutOpeningTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var scan = await Counter(rewind, new NoDatabase()).ScanCaseAsync(MemberCard, ct);

        Assert.Equal(new CaseScan(CaseScanStatus.NoSuchCase, null), scan);
    }

    [Theory]
    [InlineData(CaseStatus.CheckedOut)]
    [InlineData(CaseStatus.Retired)]
    public async Task ScanCaseAsync_CaseNotOnTheShelf_IsNotOnShelf(CaseStatus status)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var (caseId, _) = await AddCaseAsync(rewind, Label, Halloween, ct);
        await SetStatusAsync(rewind, caseId, status, ct);

        var scan = await Counter(rewind).ScanCaseAsync(Label, ct);

        Assert.Equal(new CaseScan(CaseScanStatus.NotOnShelf, null), scan);
    }

    [Fact]
    public async Task ScanCaseAsync_Cancelled_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var scan = () => Counter(rewind).ScanCaseAsync(Label, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(scan);
    }

    [Fact]
    public async Task ScanCaseAsync_NullLabel_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // NET-1: null on purpose, past the compiler's check, since this tests what ScanCaseAsync does with one.
        var scan = () => Counter(rewind).ScanCaseAsync(null!, ct);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(scan);
        Assert.Equal("storeLabel", refusal.ParamName);
    }

    // Build Plan 3.3's Done when: a box-set pick at $2.00, popcorn at $1.00 and a late fee at $1.00 come to
    // $4.00, and $10.00 leaves $6.00, counted back as $5.00 and $10.00.
    [Fact]
    public async Task CompleteAsync_DoneWhenSale_GivesTheTotalAndTheChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(ticket, ct);

        Assert.Equal((SaleStatus.Completed, 400), (result.Status, result.TotalCents));
        var sale = Assert.IsType<CompletedSale>(result.Sale);
        Assert.Equal((400, 1000, 600), (sale.TotalCents, sale.CashTenderedCents, sale.ChangeCents));
        Assert.Equal([(500, 1), (100, 1)], sale.Breakdown);
        Assert.Equal([500, 1000], sale.CountBack);
    }

    // DATA-7: one rental, one item, two lines, and the case's new status, as they were rung up (DATA-10).
    [Fact]
    public async Task CompleteAsync_DoneWhenSale_SavesTheRentalItsItemItsLinesAndTheCase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(ticket, ct);

        await using var db = rewind.OpenDb();
        var rental = await db.Rentals.AsNoTracking().Include(r => r.Items).Include(r => r.Lines).SingleAsync(ct);
        Assert.Equal(
            (result.Sale?.RentalId, ticket.MemberId, ticket.BusinessDayId, 400, 1000, 600),
            ((int?)rental.Id, rental.MemberId, rental.BusinessDayId, rental.TotalCents, rental.CashTenderedCents, rental.ChangeCents));
        var item = Assert.Single(rental.Items);
        Assert.Equal((ticket.Items[0].CaseId, ticket.Items[0].MovieId, 200), (item.CaseId, item.MovieId, item.PriceCents));
        Assert.Equal(
            [("Popcorn", 100, ticket.Lines[0].ConcessionItemId, (int?)null), ("Late fee: Halloween (1978)", 100, (int?)null, ticket.Lines[1].LateFeeDrawId)],
            rental.Lines.OrderBy(l => l.Id).Select(l => (l.Name, l.PriceCents, l.ConcessionItemId, l.LateFeeDrawId)));
        Assert.Equal(CaseStatus.CheckedOut, await StatusOfAsync(rewind, ticket.Items[0].CaseId, ct));
    }

    [Fact]
    public async Task CompleteAsync_Completed_SavesTheClocksTimeInUtc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);

        await Counter(rewind).CompleteAsync(ticket, ct);

        await using var db = rewind.OpenDb();
        var rentedAt = await db.Rentals.AsNoTracking().Select(r => r.RentedAt).SingleAsync(ct);
        Assert.Equal(rewind.Time.GetUtcNow().UtcDateTime, rentedAt);
        Assert.Equal(DateTimeKind.Utc, rentedAt.Kind);
    }

    [Fact]
    public async Task CompleteAsync_CashExactlyTheTotal_HasNoChangeToGive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct) with { CashTenderedCents = 400 };

        var result = await Counter(rewind).CompleteAsync(ticket, ct);

        var sale = Assert.IsType<CompletedSale>(result.Sale);
        Assert.Equal(0, sale.ChangeCents);
        Assert.Empty(sale.Breakdown);
        Assert.Empty(sale.CountBack);
    }

    // A cent short: the screen's dialog names the total, and the sale stays open.
    [Fact]
    public async Task CompleteAsync_CashUnderTheTotal_IsCashShortAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct) with { CashTenderedCents = 399 };

        var result = await Counter(rewind).CompleteAsync(ticket, ct);

        Assert.Equal(new SaleResult(SaleStatus.CashShort, 400, null, null), result);
        await AssertNothingSavedAsync(rewind, ct);
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, ticket.Items[0].CaseId, ct));
    }

    [Fact]
    public async Task CompleteAsync_EmptyTicket_IsEmptyTicketAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        var sale = await StartedAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(TicketFor(sale, [], [], 1000), ct);

        Assert.Equal(new SaleResult(SaleStatus.EmptyTicket, 0, null, null), result);
        await AssertNothingSavedAsync(rewind, ct);
    }

    // A snack run: nothing to rent, but something to pay for.
    [Fact]
    public async Task CompleteAsync_SnacksAlone_Completes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        var snack = await AddSnackAsync(rewind, ct);
        var sale = await StartedAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(TicketFor(sale, [], [SnackLine(snack)], 100), ct);

        Assert.Equal(SaleStatus.Completed, result.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal(1, await db.SaleLines.CountAsync(ct));
    }

    // An Overdue card with nothing else to buy still pays its fee, and a second checkout then owes nothing.
    [Fact]
    public async Task CompleteAsync_FeeAlone_Completes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        var sale = await StartedAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(TicketFor(sale, [], [FeeLine(sale)], 100), ct);

        Assert.Equal(SaleStatus.Completed, result.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal(sale.Draw.Id, await db.SaleLines.AsNoTracking().Select(l => l.LateFeeDrawId).SingleAsync(ct));
    }

    // SaleLine.MaxNameLength leaves room for the longest title Rewind keeps, with its year.
    [Fact]
    public async Task CompleteAsync_FeeLineForTheLongestTitle_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await AddCaseAsync(rewind, Label, [new Movie { Title = new string('x', TmdbClient.MaxNameLength), Year = 1978 }], ct);
        var sale = await StartedAsync(rewind, ct);

        var result = await Counter(rewind).CompleteAsync(TicketFor(sale, [], [FeeLine(sale)], 100), ct);

        Assert.Equal(SaleStatus.Completed, result.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal(SaleLine.MaxNameLength, (await db.SaleLines.AsNoTracking().SingleAsync(ct)).Name.Length);
    }

    // The clerk scanned the case when it was on the shelf, and it left before the sale could save. Complete's
    // transaction holds the write lock from before it reads, so this is the only moment it can happen, and
    // the read inside the transaction has to see it.
    [Fact]
    public async Task CompleteAsync_CaseLeftTheShelfJustBeforeTheSale_IsNotOnShelfAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);
        var caseId = ticket.Items[0].CaseId;
        var flips = new BeforeEachTransaction(rewind, token => SetStatusAsync(rewind, caseId, CaseStatus.Retired, token));

        var result = await Counter(rewind, flips).CompleteAsync(ticket, ct);

        Assert.Equal(new SaleResult(SaleStatus.NotOnShelf, 400, Label, null), result);
        await AssertNothingSavedAsync(rewind, ct);
        Assert.Equal(CaseStatus.Retired, await StatusOfAsync(rewind, caseId, ct));
    }

    // DATA-7: one case off the shelf stops the whole sale, so the other case stays where it is.
    [Fact]
    public async Task CompleteAsync_OneOfTwoCasesLeftTheShelf_LeavesTheOtherOnIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        var (firstCase, firstMovies) = await AddCaseAsync(rewind, Label, Halloween, ct);
        var (secondCase, secondMovies) = await AddCaseAsync(rewind, OtherLabel, [new Movie { Title = "Tommy Boy", Year = 1995 }], ct);
        var sale = await StartedAsync(rewind, ct);
        await SetStatusAsync(rewind, secondCase, CaseStatus.CheckedOut, ct);
        TicketItem[] items =
        [
            new TicketItem(firstCase, Label, firstMovies[0], "Halloween", 1978, 200),
            new TicketItem(secondCase, OtherLabel, secondMovies[0], "Tommy Boy", 1995, 200),
        ];

        var result = await Counter(rewind).CompleteAsync(TicketFor(sale, items, [], 1000), ct);

        Assert.Equal(new SaleResult(SaleStatus.NotOnShelf, 400, OtherLabel, null), result);
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, firstCase, ct));
        await AssertNothingSavedAsync(rewind, ct);
    }

    // Build Plan 3.3's Done when, the popcorn test proper: the next sale has the new price, and the earlier
    // sale's line keeps the old one (DATA-10).
    [Fact]
    public async Task CompleteAsync_SnackPriceChangedBetweenSales_EachLineKeepsItsOwnPrice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        var snack = await AddSnackAsync(rewind, ct);
        var bar = new ConcessionBar(rewind.DbFactory, NullLogger<ConcessionBar>.Instance);
        var sale = await StartedAsync(rewind, ct);
        await Counter(rewind).CompleteAsync(TicketFor(sale, [], [SnackLine(snack)], 1000), ct);

        await bar.SetPriceAsync(snack.Id, "1.50", ct);
        var repriced = Assert.IsType<ConcessionRow>(await bar.FindByCodeAsync(snack.Code, ct));
        await Counter(rewind).CompleteAsync(TicketFor(await StartedAsync(rewind, ct), [], [SnackLine(repriced)], 1000), ct);

        await using var db = rewind.OpenDb();
        Assert.Equal([100, 150], await db.SaleLines.AsNoTracking().OrderBy(l => l.Id).Select(l => l.PriceCents).ToListAsync(ct));
    }

    // And the same for the rental price, which each sale's Start gives the screen.
    [Fact]
    public async Task CompleteAsync_RentalPriceChangedBetweenSales_EachItemKeepsItsOwnPrice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, ClearBag, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, [new Movie { Title = "Tommy Boy", Year = 1995 }], ct);
        var earlier = await StartedAsync(rewind, ct);
        var first = ItemFor(await FoundAsync(rewind, Label, ct), earlier);
        await Counter(rewind).CompleteAsync(TicketFor(earlier, [first], [], 1000), ct);

        await SavePricesAsync(rewind, "3.00", "1.00", ct);
        var later = await StartedAsync(rewind, ct);
        var second = ItemFor(await FoundAsync(rewind, OtherLabel, ct), later);
        await Counter(rewind).CompleteAsync(TicketFor(later, [second], [], 1000), ct);

        await using var db = rewind.OpenDb();
        Assert.Equal([200, 300], await db.RentalItems.AsNoTracking().OrderBy(i => i.Id).Select(i => i.PriceCents).ToListAsync(ct));
    }

    // NET-7: the save hears the token, and the transaction takes everything with it.
    [Fact]
    public async Task CompleteAsync_CancelledJustBeforeTheSave_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);
        using var leaving = new CancellationTokenSource();
        var counter = Counter(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var complete = () => counter.CompleteAsync(ticket, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(complete);
        await AssertNothingSavedAsync(rewind, ct);
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, ticket.Items[0].CaseId, ct));
    }

    // ADR-0010: one clerk can't cause this, so it's not an expected result. The transaction takes the rental
    // and the case's new status with it (DATA-7).
    [Fact]
    public async Task CompleteAsync_SecondFeeLineForOneDraw_LetsTheRefusalThrough()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, OverdueBag, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        var sale = await StartedAsync(rewind, ct);
        await Counter(rewind).CompleteAsync(TicketFor(sale, [], [FeeLine(sale)], 100), ct);
        var found = await FoundAsync(rewind, Label, ct);

        var complete = () => Counter(rewind).CompleteAsync(TicketFor(sale, [ItemFor(found, sale)], [FeeLine(sale)], 1000), ct);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, complete);
        await using var db = rewind.OpenDb();
        Assert.Equal(1, await db.Rentals.CountAsync(ct));
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, found.CaseId, ct));
    }

    // OBS-3: a checkout is a key event.
    [Fact]
    public async Task CompleteAsync_Completed_LogsTheSale()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct);
        var log = new ListLogger<CheckoutCounter>();

        await Counter(rewind, logger: log).CompleteAsync(ticket, ct);

        Assert.Equal(
            [(LogLevel.Information, "Completed rental 1 for member 1 on business day 1: items 1, lines 2, total 400, cash 1000, change 600", (Exception?)null)],
            log.Entries);
    }

    // A refused sale changes nothing, so there's nothing for the log to say.
    [Fact]
    public async Task CompleteAsync_CashUnderTheTotal_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ticket = await DoneWhenTicketAsync(rewind, ct) with { CashTenderedCents = 399 };
        var log = new ListLogger<CheckoutCounter>();

        await Counter(rewind, logger: log).CompleteAsync(ticket, ct);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task CompleteAsync_NullTicket_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // NET-1: null on purpose, past the compiler's check, since this tests what CompleteAsync does with one.
        var complete = () => Counter(rewind).CompleteAsync(null!, ct);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(complete);
        Assert.Equal("ticket", refusal.ParamName);
    }

    private static CheckoutCounter Counter(
        TempRewind rewind,
        IDbContextFactory<RewindDb>? dbFactory = null,
        ILateFeeDraws? draws = null,
        ILogger<CheckoutCounter>? logger = null) =>
        new(
            dbFactory ?? rewind.DbFactory,
            new MemberRoster(rewind.DbFactory, NullLogger<MemberRoster>.Instance),
            draws ?? Draws(rewind),
            new StorePrices(rewind.DbFactory, NullLogger<StorePrices>.Instance),
            rewind.Time,
            logger ?? NullLogger<CheckoutCounter>.Instance);

    private static LateFeeDraws Draws(TempRewind rewind) =>
        new(rewind.DbFactory, new BusinessDays(rewind.DbFactory, rewind.Time, NullLogger<BusinessDays>.Instance), rewind.Time, rewind.Random, NullLogger<LateFeeDraws>.Instance);

    private static async Task<OpenedSale> StartedAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var start = await Counter(rewind).StartAsync(MemberCard, cancellationToken);
        return Assert.IsType<OpenedSale>(start.Sale);
    }

    private static async Task<CaseToRent> FoundAsync(TempRewind rewind, string label, CancellationToken cancellationToken)
    {
        var scan = await Counter(rewind).ScanCaseAsync(label, cancellationToken);
        return Assert.IsType<CaseToRent>(scan.Case);
    }

    private static Ticket TicketFor(OpenedSale sale, IReadOnlyList<TicketItem> items, IReadOnlyList<TicketLine> lines, int cash) =>
        new(sale.Draw.MemberId, sale.Draw.BusinessDayId, items, lines, cash);

    private static TicketItem ItemFor(CaseToRent found, OpenedSale sale, int pick = 0) =>
        new(found.CaseId, found.StoreLabel, found.Movies[pick].MovieId, found.Movies[pick].Title, found.Movies[pick].Year, sale.RentalPriceCents);

    private static TicketLine SnackLine(ConcessionRow snack) => new(snack.Name, snack.PriceCents, snack.Id, null);

    private static TicketLine FeeLine(OpenedSale sale)
    {
        var fee = Assert.IsType<FeeDue>(sale.Fee);
        return new TicketLine(fee.LineName, fee.PriceCents, null, sale.Draw.Id);
    }

    // Build Plan 3.3's sale, with cash of $10.00, as the screen would build it from Core's answers: an Overdue
    // member, a box set with its second movie picked, and popcorn at $1.00. Seed 42 names Halloween in the fee.
    private static async Task<Ticket> DoneWhenTicketAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await AddMemberAsync(rewind, OverdueBag, cancellationToken);
        Movie[] boxSet = [new Movie { Title = "Scream", Year = 1996 }, new Movie { Title = "Halloween", Year = 1978 }];
        await AddCaseAsync(rewind, Label, boxSet, cancellationToken);
        var snack = await AddSnackAsync(rewind, cancellationToken);
        var sale = await StartedAsync(rewind, cancellationToken);
        var found = await FoundAsync(rewind, Label, cancellationToken);
        return TicketFor(sale, [ItemFor(found, sale, pick: 1)], [SnackLine(snack), FeeLine(sale)], 1000);
    }

    private static async Task<int> AddMemberAsync(TempRewind rewind, string bag, CancellationToken cancellationToken, bool isActive = true, string card = MemberCard)
    {
        await using var db = rewind.OpenDb();
        var member = new Member { Name = "Dad", CardBarcode = card, Bag = bag, IsActive = isActive };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member.Id;
    }

    // Copies of the movies, so a test's array can be shared and each database gets rows of its own.
    private static async Task<(int CaseId, int[] Movies)> AddCaseAsync(TempRewind rewind, string label, Movie[] movies, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var shelved = new Case { StoreLabel = label };
        var copies = movies.Select(m => new Movie { Title = m.Title, Year = m.Year }).ToList();
        copies.ForEach(shelved.Movies.Add);
        db.Cases.Add(shelved);
        await db.SaveChangesAsync(cancellationToken);
        return (shelved.Id, [.. copies.Select(m => m.Id)]);
    }

    private static async Task<ConcessionRow> AddSnackAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var snack = new ConcessionItem { Name = "Popcorn", Code = "CON-POPCORN", PriceCents = 100 };
        db.ConcessionItems.Add(snack);
        await db.SaveChangesAsync(cancellationToken);
        return new ConcessionRow(snack.Id, snack.Name, snack.Code, snack.PriceCents, snack.IsActive);
    }

    private static async Task SavePricesAsync(TempRewind rewind, string rentalPrice, string lateFee, CancellationToken cancellationToken) =>
        await new StorePrices(rewind.DbFactory, NullLogger<StorePrices>.Instance).SaveAsync(rentalPrice, lateFee, cancellationToken);

    private static async Task SetStatusAsync(TempRewind rewind, int caseId, CaseStatus status, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        (await db.Cases.SingleAsync(c => c.Id == caseId, cancellationToken)).Status = status;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<CaseStatus> StatusOfAsync(TempRewind rewind, int caseId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.Cases.AsNoTracking().Where(c => c.Id == caseId).Select(c => c.Status).SingleAsync(cancellationToken);
    }

    private static async Task AssertNothingDrawnAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        Assert.False(await db.LateFeeDraws.AnyAsync(cancellationToken));
        Assert.False(await db.BusinessDays.AnyAsync(cancellationToken));
    }

    private static async Task AssertNothingSavedAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        Assert.False(await db.Rentals.AnyAsync(cancellationToken));
        Assert.False(await db.RentalItems.AnyAsync(cancellationToken));
        Assert.False(await db.SaleLines.AnyAsync(cancellationToken));
    }

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("The checkout counter opened the database.");
    }

    private sealed class HearsTheToken(ILateFeeDraws draws) : ILateFeeDraws
    {
        public CancellationToken Token { get; private set; }

        public Task<LateFeeDrawRow> DrawAsync(int memberId, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return draws.DrawAsync(memberId, cancellationToken);
        }
    }

    private sealed class LeavesOnceDrawn(ILateFeeDraws draws, CancellationTokenSource leaving) : ILateFeeDraws
    {
        public async Task<LateFeeDrawRow> DrawAsync(int memberId, CancellationToken cancellationToken)
        {
            var draw = await draws.DrawAsync(memberId, cancellationToken);
            await leaving.CancelAsync();
            return draw;
        }
    }
}
