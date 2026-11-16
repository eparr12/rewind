using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;
using Rewind.Core.Returns;
using Rewind.Core.StoreManager;
using Rewind.Core.Tests.Checkout;

namespace Rewind.Core.Tests.Returns;

public sealed class ReturnDeskTests
{
    private const string DadsCard = "MEM-001";
    private const string Label = "RV-00042";
    private const string OtherLabel = "RV-00043";
    private const string FactoryUpc = "032429258373";
    private const int Cash = 1000;

    // A bag with one card left, a Clear one, so Dad's sale has no late fee on it.
    private const string ClearBag = "C";

    // The night opens at 10:15 PM, so two hours later the register's clock is past midnight: a sale then is on the
    // next day's date, and still on the night before (DATA-8).
    private static readonly TimeSpan PastMidnight = TimeSpan.FromHours(2);

    private static readonly Movie[] Halloween = [new Movie { Title = "Halloween", Year = 1978 }];
    private static readonly Movie[] Scream = [new Movie { Title = "Scream", Year = 1996 }];

    // Halloween is added first and listed first, so Scream, the one picked, is the case's first movie neither by Id
    // nor by title.
    private static readonly Movie[] BoxSet = [new Movie { Title = "Halloween", Year = 1978 }, new Movie { Title = "Scream", Year = 1996 }];

    // Build Plan 3.4: scanning a case puts it back On shelf and stamps its return time, an hour after the sale.
    [Fact]
    public async Task CheckInAsync_CaseThatsOut_PutsItOnTheShelfWithTheClocksTimeInUtc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        rewind.Time.Advance(TimeSpan.FromHours(1));

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(CheckInStatus.CheckedIn, checkIn.Status);
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, Label, ct));
        var returnedAt = Assert.Single(await ItemsAsync(rewind, ct)).ReturnedAt;
        Assert.Equal(((DateTime?)rewind.Time.GetUtcNow().UtcDateTime, (DateTimeKind?)DateTimeKind.Utc), (returnedAt, returnedAt?.Kind));
    }

    // What the screen asks who watched with. The night is the one Dad rented on, which opened before midnight,
    // though the sale came after it (DATA-8). Grandpa has been deactivated, so he isn't asked. Another case was
    // added first, so the box set's case and its rental item have different Ids.
    [Fact]
    public async Task CheckInAsync_CaseThatsOut_NamesTheMoviePickedTheRenterTheNightAndWhoToAsk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, OtherLabel, Halloween, ct);
        await AddCaseAsync(rewind, Label, BoxSet, ct);
        var nightOpened = rewind.Time.GetUtcNow().UtcDateTime;
        await RentAsync(rewind, ct, Label);

        var rental = await CheckedInAsync(rewind, Label, ct);

        var item = Assert.Single(await ItemsAsync(rewind, ct));
        Assert.Equal((item.Id, Label, "Scream", (int?)1996, "Dad"), (rental.RentalItemId, rental.StoreLabel, rental.Title, rental.Year, rental.RentedBy));
        Assert.Equal((nightOpened, DateTimeKind.Utc), (rental.NightOpenedAt, rental.NightOpenedAt.Kind));
        Assert.Equal(["ava", "Dad", "Mom"], rental.People.Select(p => p.Name));
    }

    // The renter is named whether they're active or not. Dad was deactivated while the case was out, so the check-in
    // still names Dad, and asks only the members who are active.
    [Fact]
    public async Task CheckInAsync_RenterDeactivatedSince_StillNamesThemAndDoesntAskThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await new MemberRoster(rewind.DbFactory, NullLogger<MemberRoster>.Instance).SetActiveAsync(family.Dad, isActive: false, ct);

        var rental = await CheckedInAsync(rewind, Label, ct);

        Assert.Equal("Dad", rental.RentedBy);
        Assert.Equal(["ava", "Mom"], rental.People.Select(p => p.Name));
    }

    [Fact]
    public async Task CheckInAsync_LabelInLowerCase_IsReadAsAScanIs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);

        var rental = await CheckedInAsync(rewind, "rv-00042", ct);

        Assert.Equal(Label, rental.StoreLabel);
    }

    [Fact]
    public async Task CheckInAsync_LabelNoCaseHas_IsNoSuchCase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.NoSuchCase, null), checkIn);
    }

    // A factory UPC is what a clerk might scan by mistake, and nothing but a store label is worth asking the
    // database about.
    [Fact]
    public async Task CheckInAsync_NotAStoreLabel_IsNoSuchCaseWithoutOpeningTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var checkIn = await Desk(rewind, new NoDatabase()).CheckInAsync(FactoryUpc, ct);

        Assert.Equal(new CheckIn(CheckInStatus.NoSuchCase, null), checkIn);
    }

    // Another case's rental is back with nobody on its watch log, and it's this case's rentals that count.
    [Fact]
    public async Task CheckInAsync_CaseNeverRented_IsAlreadyOnShelfWithNothingToAsk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await RentAsync(rewind, ct, OtherLabel);
        await CheckedInAsync(rewind, OtherLabel, ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.AlreadyOnShelf, null), checkIn);
    }

    // ADR-0012: nobody answered who watched, whether the clerk skipped it, pressed Esc by mistake or left the screen,
    // so the rescan asks about that rental. The rental's other case was added first and has its watch log, so
    // neither its rows nor this case's Id can stand in for this case's rental item.
    [Fact]
    public async Task CheckInAsync_CaseBackWithAnEmptyWatchLog_IsAlreadyOnShelfAndAsksAboutThatRental()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label, OtherLabel);
        var returned = await CheckedInAsync(rewind, Label, ct);
        var other = await CheckedInAsync(rewind, OtherLabel, ct);
        await Desk(rewind).LogWhoWatchedAsync(other.RentalItemId, [new Watcher(family.Mom, 3)], ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(CheckInStatus.AlreadyOnShelf, checkIn.Status);
        Assert.Equal(returned.RentalItemId, checkIn.Rental?.RentalItemId);
    }

    [Fact]
    public async Task CheckInAsync_CaseBackWithItsWatchLogSaved_IsAlreadyOnShelfWithNothingToAsk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        var returned = await CheckedInAsync(rewind, Label, ct);
        await Desk(rewind).LogWhoWatchedAsync(returned.RentalItemId, [new Watcher(family.Dad, 5)], ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.AlreadyOnShelf, null), checkIn);
    }

    // ADR-0012: only the case's last rental is asked about. Its first rental's watch log was skipped and its second's
    // was answered, so there's nothing left to ask.
    [Fact]
    public async Task CheckInAsync_CaseRentedAgainSince_DoesntAskAboutTheEarlierRental()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await CheckedInAsync(rewind, Label, ct);
        await RentAsync(rewind, ct, Label);
        var again = await CheckedInAsync(rewind, Label, ct);
        await Desk(rewind).LogWhoWatchedAsync(again.RentalItemId, [new Watcher(family.Dad, 4)], ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.AlreadyOnShelf, null), checkIn);
    }

    // Nothing retires a case yet, so this one was retired by hand while it was out. It stays as it was: retired, with
    // its rental still open.
    [Fact]
    public async Task CheckInAsync_RetiredCase_IsRefusedAndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await SetStatusAsync(rewind, Label, CaseStatus.Retired, ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.Retired, null), checkIn);
        Assert.Equal(CaseStatus.Retired, await StatusOfAsync(rewind, Label, ct));
        Assert.Null(Assert.Single(await ItemsAsync(rewind, ct)).ReturnedAt);
    }

    // Due back the night it went out, and back the next day, after Close Out. A return needs no night, so none
    // opens, and nothing is charged: late fees are the shuffle bag's.
    [Fact]
    public async Task CheckInAsync_CaseBackAfterItsNightClosed_ChecksItInAndOpensNoNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await Days(rewind).CloseAsync(ct);
        rewind.Time.Advance(TimeSpan.FromDays(1));

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(CheckInStatus.CheckedIn, checkIn.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal([true], await db.BusinessDays.Select(d => d.ClosedAt != null).ToListAsync(ct));
        Assert.Equal(1, await db.Rentals.CountAsync(ct));
        Assert.False(await db.SaleLines.AnyAsync(ct));
    }

    // Only a hand edit leaves a case marked checked out with no rental out. No rental came back, so nobody is asked,
    // though the case's last rental has an empty watch log.
    [Fact]
    public async Task CheckInAsync_CaseCheckedOutWithNoRentalOut_PutsItOnTheShelfWithNothingToAsk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await CheckedInAsync(rewind, Label, ct);
        await SetStatusAsync(rewind, Label, CaseStatus.CheckedOut, ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(new CheckIn(CheckInStatus.Reshelved, null), checkIn);
        Assert.Equal(CaseStatus.OnShelf, await StatusOfAsync(rewind, Label, ct));
    }

    // OBS-3: a handled problem, and the log is the only place the hand edit shows.
    [Fact]
    public async Task CheckInAsync_CaseCheckedOutWithNoRentalOut_LogsAWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await SetStatusAsync(rewind, Label, CaseStatus.CheckedOut, ct);
        var log = new ListLogger<ReturnDesk>();

        await Desk(rewind, logger: log).CheckInAsync(Label, ct);

        Assert.Equal(
            [(LogLevel.Warning, "Case RV-00042 was checked out with no rental out, which only a hand edit leaves, so it's back on the shelf", (Exception?)null)],
            log.Entries);
    }

    // The other hand edit: the case was put On shelf by hand with its rental still open. The open rental is what says
    // the case is out, so the scan checks it in, which lets the case rent again (DATA-11).
    [Fact]
    public async Task CheckInAsync_CaseOnTheShelfWithItsRentalStillOpen_ChecksItIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        await SetStatusAsync(rewind, Label, CaseStatus.OnShelf, ct);

        var checkIn = await Desk(rewind).CheckInAsync(Label, ct);

        Assert.Equal(CheckInStatus.CheckedIn, checkIn.Status);
        Assert.NotNull(Assert.Single(await ItemsAsync(rewind, ct)).ReturnedAt);
    }

    // OBS-3: the scan puts the hand edit right, so the log is the only place it shows, beside the check-in's own line.
    // It's the rental's second case, so its item and its rental have different numbers.
    [Fact]
    public async Task CheckInAsync_CaseOnTheShelfWithItsRentalStillOpen_LogsAWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await RentAsync(rewind, ct, Label, OtherLabel);
        await SetStatusAsync(rewind, OtherLabel, CaseStatus.OnShelf, ct);
        var log = new ListLogger<ReturnDesk>();

        await Desk(rewind, logger: log).CheckInAsync(OtherLabel, ct);

        Assert.Equal(
            [
                (LogLevel.Warning, "Case RV-00043 was on the shelf with rental item 2 still open, which only a hand edit leaves, so it's checked in", (Exception?)null),
                (LogLevel.Information, "Checked in case RV-00043, rental item 2 of rental 1", null),
            ],
            log.Entries);
    }

    // Each case is scanned on its own and closes only its own item, so the other stays out until it's scanned. The
    // rental's second case comes back first, so its first open item isn't the right one.
    [Fact]
    public async Task CheckInAsync_TwoCasesOnOneRental_ChecksEachInOnItsOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await RentAsync(rewind, ct, Label, OtherLabel);
        var items = await ItemsAsync(rewind, ct);

        var second = await CheckedInAsync(rewind, OtherLabel, ct);
        var firstWhileTheSecondIsBack = await StatusOfAsync(rewind, Label, ct);
        var first = await CheckedInAsync(rewind, Label, ct);

        Assert.Equal(CaseStatus.CheckedOut, firstWhileTheSecondIsBack);
        Assert.Equal([items[1].Id, items[0].Id], [second.RentalItemId, first.RentalItemId]);
    }

    // NET-7: the save hears the token, and the transaction takes the return time and the case's status with it.
    [Fact]
    public async Task CheckInAsync_CancelledJustBeforeTheSave_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        using var leaving = new CancellationTokenSource();
        var desk = Desk(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var checkIn = () => desk.CheckInAsync(Label, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(checkIn);
        Assert.Null(Assert.Single(await ItemsAsync(rewind, ct)).ReturnedAt);
        Assert.Equal(CaseStatus.CheckedOut, await StatusOfAsync(rewind, Label, ct));
    }

    // NET-7: the clerk left just as the return committed. The case is back whatever happens next, so the answer comes
    // back too, and the screen decides what to do with it (ADR-0011).
    [Fact]
    public async Task CheckInAsync_CancelledOnceCommitted_StillAnswers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        using var leaving = new CancellationTokenSource();
        var desk = Desk(rewind, new AfterEachCommit(rewind, _ => leaving.CancelAsync()));

        var checkIn = await desk.CheckInAsync(Label, leaving.Token);

        Assert.True(leaving.IsCancellationRequested); // the cancel came, once the return was committed
        Assert.Equal(CheckInStatus.CheckedIn, checkIn.Status);
        Assert.NotNull(checkIn.Rental);
    }

    // A second register, or a check-in that landed a blink before this one's transaction began. The read inside the
    // transaction sees it, so the case comes back once, at the first one's time.
    [Fact]
    public async Task CheckInAsync_AnotherCheckInGetsInFirst_IsAlreadyOnShelfWithOneReturnTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        var firstBack = rewind.Time.GetUtcNow().UtcDateTime;
        var first = new BeforeEachTransaction(rewind, async token =>
        {
            await Desk(rewind).CheckInAsync(Label, token);
            rewind.Time.Advance(TimeSpan.FromMinutes(1)); // so a second return time would differ
        });

        var checkIn = await Desk(rewind, first).CheckInAsync(Label, ct);

        Assert.Equal(CheckInStatus.AlreadyOnShelf, checkIn.Status);
        Assert.Equal(firstBack, Assert.Single(await ItemsAsync(rewind, ct)).ReturnedAt);
    }

    // OBS-3: a return is a key event. The rental's second case comes back, so its item and its rental have different
    // numbers.
    [Fact]
    public async Task CheckInAsync_CheckedIn_LogsTheReturn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await RentAsync(rewind, ct, Label, OtherLabel);
        var log = new ListLogger<ReturnDesk>();

        await Desk(rewind, logger: log).CheckInAsync(OtherLabel, ct);

        Assert.Equal([(LogLevel.Information, "Checked in case RV-00043, rental item 2 of rental 1", (Exception?)null)], log.Entries);
    }

    // A scan that changes nothing leaves nothing for the log to say.
    [Fact]
    public async Task CheckInAsync_AlreadyOnShelf_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        var log = new ListLogger<ReturnDesk>();

        await Desk(rewind, logger: log).CheckInAsync(Label, ct);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task CheckInAsync_NullLabel_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // NET-1: null on purpose, past the compiler's check, since this tests what CheckInAsync does with one.
        var checkIn = () => Desk(rewind).CheckInAsync(null!, ct);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(checkIn);
        Assert.Equal("storeLabel", refusal.ParamName);
    }

    // Build Plan 3.4's Done when: a returned movie shows in the watch log with each person's stars. Dad rents Scream
    // from a box set at the real counter, and once it's checked in, Dad gives it 5 stars, Mom 4, and Ava none.
    [Fact]
    public async Task LogWhoWatchedAsync_DoneWhenReturn_PutsTheMovieInTheWatchLogWithEachPersonsStars()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, BoxSet, ct);
        await RentAsync(rewind, ct, Label);
        var rental = await CheckedInAsync(rewind, Label, ct);

        var logged = await Desk(rewind).LogWhoWatchedAsync(
            rental.RentalItemId,
            [new Watcher(family.Dad, 5), new Watcher(family.Mom, 4), new Watcher(family.Ava, null)],
            ct);

        Assert.Equal(WatchLogStatus.Logged, logged);
        await using var db = rewind.OpenDb();
        var watched = await (
            from viewing in db.WatchLog
            join item in db.RentalItems on viewing.RentalItemId equals item.Id
            join movie in db.Movies on item.MovieId equals movie.Id
            join member in db.Members on viewing.MemberId equals member.Id
            orderby viewing.Id
            select new { movie.Title, member.Name, viewing.Stars })
            .ToListAsync(ct);
        Assert.Equal([("Scream", "Dad", (int?)5), ("Scream", "Mom", 4), ("Scream", "ava", null)], watched.Select(v => (v.Title, v.Name, v.Stars)));
    }

    [Theory]
    [InlineData(Viewing.MinStars)]
    [InlineData(Viewing.MaxStars)]
    public async Task LogWhoWatchedAsync_StarsAtTheirLimits_AreLogged(int stars)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);

        var logged = await Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, stars)], ct);

        Assert.Equal(WatchLogStatus.Logged, logged);
        Assert.Equal([(family.Dad, (int?)stars)], await WatchLogAsync(rewind, ct));
    }

    // ADR-0012: a watch log is saved once. The second answer names Ava, whom the unique index would let through, so
    // only the desk's own check can stop it.
    [Fact]
    public async Task LogWhoWatchedAsync_SecondAnswerForOneItem_IsAlreadyLoggedAndAddsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);
        await Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, 5), new Watcher(family.Mom, 4)], ct);

        var second = await Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Ava, 3)], ct);

        Assert.Equal(WatchLogStatus.AlreadyLogged, second);
        Assert.Equal([(family.Dad, (int?)5), (family.Mom, 4)], await WatchLogAsync(rewind, ct));
    }

    // Saved once is for each rental item: the rental's other case has its watch log, and this one's is still empty.
    [Fact]
    public async Task LogWhoWatchedAsync_AnotherItemOnTheRentalLogged_LogsThisOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await AddCaseAsync(rewind, OtherLabel, Scream, ct);
        await RentAsync(rewind, ct, Label, OtherLabel);
        var first = await CheckedInAsync(rewind, Label, ct);
        var second = await CheckedInAsync(rewind, OtherLabel, ct);
        await Desk(rewind).LogWhoWatchedAsync(first.RentalItemId, [new Watcher(family.Dad, 5)], ct);

        var logged = await Desk(rewind).LogWhoWatchedAsync(second.RentalItemId, [new Watcher(family.Dad, 4)], ct);

        Assert.Equal(WatchLogStatus.Logged, logged);
        Assert.Equal([(family.Dad, (int?)5), (family.Dad, 4)], await WatchLogAsync(rewind, ct));
    }

    // The same rule against a second register: Mom's answer landed a blink before this one's transaction began, and
    // the read inside the transaction sees it.
    [Fact]
    public async Task LogWhoWatchedAsync_AnotherAnswerGetsInFirst_IsAlreadyLoggedAndAddsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);
        var first = new BeforeEachTransaction(rewind, token => Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Mom, 4)], token));

        var logged = await Desk(rewind, first).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, 5)], ct);

        Assert.Equal(WatchLogStatus.AlreadyLogged, logged);
        Assert.Equal([(family.Mom, (int?)4)], await WatchLogAsync(rewind, ct));
    }

    // ADR-0012: nothing saved reads as a skip, so the case's next scan asks again. With nothing to save, there's
    // nothing to look up either.
    [Fact]
    public async Task LogWhoWatchedAsync_NobodyOnTheList_IsNoWatchersWithoutOpeningTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var logged = await Desk(rewind, new NoDatabase()).LogWhoWatchedAsync(SaleParts.NoSuchId, [], ct);

        Assert.Equal(WatchLogStatus.NoWatchers, logged);
    }

    // The screen offers stars in range, so this is a bug. Mom's row is the bad one, and Dad's isn't saved either.
    [Theory]
    [InlineData(Viewing.MinStars - 1)]
    [InlineData(Viewing.MaxStars + 1)]
    public async Task LogWhoWatchedAsync_StarsOutOfRange_Throw(int stars)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);

        var log = () => Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, 5), new Watcher(family.Mom, stars)], ct);

        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(log);
        Assert.Equal("watchers", refusal.ParamName);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // The screen lists each member once, so this is a bug too, whatever stars each row has.
    [Fact]
    public async Task LogWhoWatchedAsync_OneMemberTwice_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);

        var log = () => Desk(rewind).LogWhoWatchedAsync(
            rental.RentalItemId,
            [new Watcher(family.Dad, 5), new Watcher(family.Mom, 4), new Watcher(family.Dad, 3)],
            ct);

        var refusal = await Assert.ThrowsAsync<ArgumentException>(log);
        Assert.Equal("watchers", refusal.ParamName);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // Rental items are never deleted (DATA-4), so an Id that isn't one is a bug.
    [Fact]
    public async Task LogWhoWatchedAsync_RentalItemThatIsntThere_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);

        var log = () => Desk(rewind).LogWhoWatchedAsync(SaleParts.NoSuchId, [new Watcher(family.Dad, 5)], ct);

        await Assert.ThrowsAsync<InvalidOperationException>(log);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // Return asks who watched only once the case is back.
    [Fact]
    public async Task LogWhoWatchedAsync_RentalItemNotBackYet_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        await AddCaseAsync(rewind, Label, Halloween, ct);
        await RentAsync(rewind, ct, Label);
        var stillOut = Assert.Single(await ItemsAsync(rewind, ct));

        var log = () => Desk(rewind).LogWhoWatchedAsync(stillOut.Id, [new Watcher(family.Dad, 5)], ct);

        await Assert.ThrowsAsync<InvalidOperationException>(log);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // DATA-7: every row goes in one save, so a row the database refuses takes the others with it. The desk's own
    // checks pass this list, and only the database knows the second member isn't there.
    [Fact]
    public async Task LogWhoWatchedAsync_OneRowTheDatabaseRefuses_SavesNoneOfThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);

        var log = () => Desk(rewind).LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, 5), new Watcher(SaleParts.NoSuchId, 4)], ct);

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, log);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // NET-7: the save hears the token, and nothing of the watch log is saved.
    [Fact]
    public async Task LogWhoWatchedAsync_CancelledJustBeforeTheSave_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);
        using var leaving = new CancellationTokenSource();
        var desk = Desk(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var log = () => desk.LogWhoWatchedAsync(rental.RentalItemId, [new Watcher(family.Dad, 5)], leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(log);
        Assert.Empty(await WatchLogAsync(rewind, ct));
    }

    // OBS-3: the watch log is the heirloom, so each one saved is worth a line.
    [Fact]
    public async Task LogWhoWatchedAsync_Logged_LogsTheWatchLog()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var family = await AddFamilyAsync(rewind, ct);
        var rental = await ReturnedAsync(rewind, ct);
        var log = new ListLogger<ReturnDesk>();

        await Desk(rewind, logger: log).LogWhoWatchedAsync(
            rental.RentalItemId,
            [new Watcher(family.Dad, 5), new Watcher(family.Mom, 4), new Watcher(family.Ava, null)],
            ct);

        Assert.Equal([(LogLevel.Information, "Logged who watched rental item 1: watchers 3", (Exception?)null)], log.Entries);
    }

    [Fact]
    public async Task LogWhoWatchedAsync_NullList_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // NET-1: null on purpose, past the compiler's check, since this tests what LogWhoWatchedAsync does with one.
        var log = () => Desk(rewind).LogWhoWatchedAsync(SaleParts.NoSuchId, null!, ct);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(log);
        Assert.Equal("watchers", refusal.ParamName);
    }

    private static ReturnDesk Desk(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null, ILogger<ReturnDesk>? logger = null) =>
        new(
            dbFactory ?? rewind.DbFactory,
            new MemberRoster(rewind.DbFactory, NullLogger<MemberRoster>.Instance),
            rewind.Time,
            logger ?? NullLogger<ReturnDesk>.Instance);

    private static CheckoutCounter Counter(TempRewind rewind) =>
        new(
            rewind.DbFactory,
            new MemberRoster(rewind.DbFactory, NullLogger<MemberRoster>.Instance),
            new LateFeeDraws(rewind.DbFactory, Days(rewind), rewind.Time, rewind.Random, NullLogger<LateFeeDraws>.Instance),
            new StorePrices(rewind.DbFactory, NullLogger<StorePrices>.Instance),
            rewind.Time,
            NullLogger<CheckoutCounter>.Instance);

    private static BusinessDays Days(TempRewind rewind) => new(rewind.DbFactory, rewind.Time, NullLogger<BusinessDays>.Instance);

    // Dad rents the cases in one sale at the real counter, which opens the night, and the sale completes past
    // midnight. From each case, the movie the screen lists last is picked.
    private static async Task RentAsync(TempRewind rewind, CancellationToken cancellationToken, params string[] labels)
    {
        var counter = Counter(rewind);
        var sale = Assert.IsType<OpenedSale>((await counter.StartAsync(DadsCard, cancellationToken)).Sale);
        var items = new List<TicketItem>();
        foreach (var label in labels)
        {
            var found = Assert.IsType<CaseToRent>((await counter.ScanCaseAsync(label, cancellationToken)).Case);
            var picked = found.Movies[^1];
            items.Add(new TicketItem(found.CaseId, found.StoreLabel, picked.MovieId, picked.Title, picked.Year, sale.RentalPriceCents));
        }

        rewind.Time.Advance(PastMidnight);
        var result = await counter.CompleteAsync(new Ticket(sale.Draw.MemberId, sale.Draw.BusinessDayId, items, [], Cash), cancellationToken);
        Assert.Equal(SaleStatus.Completed, result.Status);
    }

    private static async Task<ReturnedRental> CheckedInAsync(TempRewind rewind, string label, CancellationToken cancellationToken)
    {
        var checkIn = await Desk(rewind).CheckInAsync(label, cancellationToken);
        Assert.Equal(CheckInStatus.CheckedIn, checkIn.Status);
        return Assert.IsType<ReturnedRental>(checkIn.Rental);
    }

    // Halloween, rented and back, with nobody on its watch log yet.
    private static async Task<ReturnedRental> ReturnedAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await AddCaseAsync(rewind, Label, Halloween, cancellationToken);
        await RentAsync(rewind, cancellationToken, Label);
        return await CheckedInAsync(rewind, Label, cancellationToken);
    }

    // Dad rents. By Id and by card, the family runs Dad, Mom, Ava, Grandpa, and the roster, which ignores case, puts
    // ava first. Grandpa has been deactivated.
    private static async Task<Family> AddFamilyAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var dad = new Member { Name = "Dad", CardBarcode = DadsCard, Bag = ClearBag };
        var mom = new Member { Name = "Mom", CardBarcode = "MEM-002" };
        var ava = new Member { Name = "ava", CardBarcode = "MEM-003" };
        var grandpa = new Member { Name = "Grandpa", CardBarcode = "MEM-004", IsActive = false };
        db.Members.AddRange(dad, mom, ava, grandpa);
        await db.SaveChangesAsync(cancellationToken);
        return new Family(dad.Id, mom.Id, ava.Id);
    }

    // Copies of the movies, so a test's array can be shared and each database gets rows of its own.
    private static async Task AddCaseAsync(TempRewind rewind, string label, Movie[] movies, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var shelved = new Case { StoreLabel = label };
        foreach (var movie in movies)
        {
            shelved.Movies.Add(new Movie { Title = movie.Title, Year = movie.Year });
        }

        db.Cases.Add(shelved);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetStatusAsync(TempRewind rewind, string label, CaseStatus status, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        (await db.Cases.SingleAsync(c => c.StoreLabel == label, cancellationToken)).Status = status;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<CaseStatus> StatusOfAsync(TempRewind rewind, string label, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.Cases.AsNoTracking().Where(c => c.StoreLabel == label).Select(c => c.Status).SingleAsync(cancellationToken);
    }

    private static async Task<List<RentalItem>> ItemsAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.RentalItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync(cancellationToken);
    }

    // Each row of the watch log as its member and their stars, in the order they were saved.
    private static async Task<List<(int MemberId, int? Stars)>> WatchLogAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var rows = await db.WatchLog.AsNoTracking().OrderBy(v => v.Id).ToListAsync(cancellationToken);
        return [.. rows.Select(v => (v.MemberId, v.Stars))];
    }

    private sealed record Family(int Dad, int Mom, int Ava);

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("The return desk opened the database.");
    }
}
