using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.Labels;

namespace Rewind.Core.Tests.Labels;

public sealed class LabelSheetsTests
{
    [Fact]
    public async Task ReserveLabelsAsync_FirstSheet_IsRV00001ToRV00030AndAdvancesTo31()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var reservation = await Sheets(rewind).ReserveLabelsAsync(ct);

        Assert.Equal(SheetStatus.Reserved, reservation.Status);
        Assert.Equal(StoreLabels(1, 30), Codes(reservation));
        Assert.Equal(31, (await NextNumbersAsync(rewind, ct)).StoreLabel);
    }

    [Fact]
    public async Task ReserveLabelsAsync_TwoSheets_NeverRepeatANumber()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var sheets = Sheets(rewind);

        var first = await sheets.ReserveLabelsAsync(ct);
        var second = await sheets.ReserveLabelsAsync(ct);

        Assert.Equal(StoreLabels(31, 30), Codes(second));
        Assert.Empty(Codes(first).Intersect(Codes(second), StringComparer.Ordinal));
    }

    // ADR-0006: sheets are only ever full, so the last sheet that fits ends on the last label there is.
    [Fact]
    public async Task ReserveLabelsAsync_LastFullSheet_EndsAtRV99999()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await SetNextStoreLabelAsync(rewind, 99_970, ct);

        var reservation = await Sheets(rewind).ReserveLabelsAsync(ct);

        Assert.Equal(StoreLabels(99_970, 30), Codes(reservation));
    }

    // One number later, the sheet would end on RV-100000, which isn't a store label (Barcodes).
    [Fact]
    public async Task ReserveLabelsAsync_PastRV99999_IsOutOfNumbersAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await SetNextStoreLabelAsync(rewind, 99_971, ct);

        var reservation = await Sheets(rewind).ReserveLabelsAsync(ct);

        Assert.Equal(new SheetReservation(SheetStatus.OutOfNumbers, null), reservation);
        Assert.Equal(99_971, (await NextNumbersAsync(rewind, ct)).StoreLabel);
    }

    [Fact]
    public async Task ReserveLabelsAsync_PastRV99999_LogsAWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await SetNextStoreLabelAsync(rewind, 99_971, ct);
        var log = new ListLogger<LabelSheets>();

        await Sheets(rewind, log).ReserveLabelsAsync(ct);

        Assert.Contains(
            (LogLevel.Warning, "No numbers are left for a sheet of store labels: the next would start at 99971, and the last is 99999", (Exception?)null),
            log.Entries);
    }

    // OBS-1: a label on two cases can be traced back to its sheet in the log a month on.
    [Fact]
    public async Task ReserveLabelsAsync_AnySheet_LogsItsFirstAndLastCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<LabelSheets>();

        await Sheets(rewind, log).ReserveLabelsAsync(ct);

        Assert.Contains(
            (LogLevel.Information, "Reserved RV-00001 to RV-00030 for a sheet of store labels", (Exception?)null),
            log.Entries);
    }

    // The store's name of record is StoreSettings', which the Store Manager will edit (Build Plan 3.1).
    [Fact]
    public async Task ReserveLabelsAsync_AnySheet_CarriesTheStoreName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            await db.StoreSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.StoreName, "Rewind Video East"), ct);
        }

        var reservation = await Sheets(rewind).ReserveLabelsAsync(ct);

        Assert.Equal("Rewind Video East", Assert.IsType<Sheet>(reservation.Sheet).StoreName);
    }

    [Fact]
    public async Task ReserveLabelsAsync_AnySheet_DrawsEachLabelAtTheLabelSize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var reservation = await Sheets(rewind).ReserveLabelsAsync(ct);

        Assert.All(Assert.IsType<Sheet>(reservation.Sheet).Barcodes, barcode => Assert.Equal(
            Code128.Svg(barcode.Code, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches),
            barcode.Svg));
    }

    // ADR-0006: the transaction, not the screen's busy guard, is what keeps two sheets made at once
    // from sharing a number. The first save waits at a gate until a second reservation asks for a
    // transaction. Without the first's own, the second has read the counter by then, and both sheets
    // would be RV-00001 to RV-00030.
    [Fact(Timeout = 30_000)] // a reservation that waits for one that waits for it never finishes
    public async Task ReserveLabelsAsync_TwoAtOnce_NeverRepeatANumber()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var gate = new FirstSaveWaitsForASecond();
        var sheets = new LabelSheets(new GatedContexts(rewind, gate), rewind.Time, NullLogger<LabelSheets>.Instance);

        var first = sheets.ReserveLabelsAsync(ct);
        await Task.WhenAny(gate.FirstHeld, first);
        var second = Task.Run(() => sheets.ReserveLabelsAsync(ct), ct);
        var reservations = await Task.WhenAll(first, second);

        var codes = reservations.SelectMany(Codes).ToList();
        Assert.Equal(60, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ReserveCardsAsync_FirstSheet_IsMEM001ToMEM010AndAdvancesTo11()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var reservation = await Sheets(rewind).ReserveCardsAsync(ct);

        Assert.Equal(SheetStatus.Reserved, reservation.Status);
        Assert.Equal(MemberCards(1, 10), Codes(reservation));
        Assert.Equal(11, (await NextNumbersAsync(rewind, ct)).MemberCard);
    }

    [Fact]
    public async Task ReserveCardsAsync_LastFullSheet_EndsAtMEM999()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await SetNextMemberCardAsync(rewind, 990, ct);

        var reservation = await Sheets(rewind).ReserveCardsAsync(ct);

        Assert.Equal(MemberCards(990, 10), Codes(reservation));
    }

    // One number later, the sheet would end on MEM-1000, which isn't a member card (Barcodes).
    [Fact]
    public async Task ReserveCardsAsync_PastMEM999_IsOutOfNumbersAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await SetNextMemberCardAsync(rewind, 991, ct);

        var reservation = await Sheets(rewind).ReserveCardsAsync(ct);

        Assert.Equal(new SheetReservation(SheetStatus.OutOfNumbers, null), reservation);
        Assert.Equal(991, (await NextNumbersAsync(rewind, ct)).MemberCard);
    }

    [Fact]
    public async Task ReserveCardsAsync_AnySheet_DrawsEachCardAtTheCardSize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var reservation = await Sheets(rewind).ReserveCardsAsync(ct);

        Assert.All(Assert.IsType<Sheet>(reservation.Sheet).Barcodes, barcode => Assert.Equal(
            Code128.Svg(barcode.Code, Code128.MemberCardWidthInches, Code128.MemberCardHeightInches),
            barcode.Svg));
    }

    // NET-7: the token goes on to the database, so leaving the Store Manager stops a sheet being made, and
    // a sheet that's stopped spends no numbers (ADR-0006).
    [Theory]
    [InlineData(SheetKind.StoreLabels)]
    [InlineData(SheetKind.MemberCards)]
    public async Task Reserve_Cancelled_SpendsNoNumbers(SheetKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var sheets = Sheets(rewind);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var reserve = () => kind == SheetKind.StoreLabels ? sheets.ReserveLabelsAsync(leaving.Token) : sheets.ReserveCardsAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(reserve);
        Assert.Equal((1, 1), await NextNumbersAsync(rewind, ct));
    }

    [Fact]
    public async Task PeekAsync_FreshDatabase_IsRV00001AndMEM001()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var next = await Sheets(rewind).PeekAsync(ct);

        Assert.Equal(new NextCodes("RV-00001", "MEM-001"), next);
    }

    // Each code comes from its own counter: on a fresh database both are 1, so only a sheet of one
    // kind tells them apart.
    [Fact]
    public async Task PeekAsync_AfterALabelSheet_IsRV00031AndMEM001()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var sheets = Sheets(rewind);
        await sheets.ReserveLabelsAsync(ct);

        var next = await sheets.PeekAsync(ct);

        Assert.Equal(new NextCodes("RV-00031", "MEM-001"), next);
    }

    // Build Plan 3.1: the menu lists each snack on sale with its price, in the order the Snacks tab lists
    // them. NOCASE, so a name typed in lower case sorts among the others.
    [Fact]
    public async Task DrawMenuAsync_SnacksOnSale_ListsThemByNameWithTheirPrices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        await AddSnackAsync(rewind, "candy", "CON-CANDY", 50, ct);
        await AddSnackAsync(rewind, "Soda", "CON-SODA", 75, ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(MenuStatus.Drawn, drawing.Status);
        Assert.Equal([("candy", 50, "CON-CANDY"), ("Popcorn", 100, "CON-POPCORN"), ("Soda", 75, "CON-SODA")], Lines(drawing));
    }

    // Two snacks can share a name, so the menu has a second order, the code, as the Snacks tab does.
    [Fact]
    public async Task DrawMenuAsync_TwoSnacksWithOneName_ListsThemByCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Soda", "CON-CANSODA", 75, ct);
        await AddSnackAsync(rewind, "Soda", "CON-BOTTLESODA", 100, ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(["CON-BOTTLESODA", "CON-CANSODA"], Lines(drawing).Select(line => line.Code));
    }

    // DATA-4: a deactivated snack keeps its code, but it isn't sold, so the menu leaves it off.
    [Fact]
    public async Task DrawMenuAsync_DeactivatedSnack_IsLeftOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        await AddSnackAsync(rewind, "Gummy worms", "CON-GUMMYWORMS", 50, ct, isActive: false);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(["CON-POPCORN"], Lines(drawing).Select(line => line.Code));
    }

    // #21: wider than a member card's, so the longest code a snack can have still prints bars a scanner reads.
    [Fact]
    public async Task DrawMenuAsync_AnySnack_DrawsItsBarcodeAtTheMenuSize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        await AddSnackAsync(rewind, "Fountain pop or SodaStream", "CON-FOUNTAINPOPORSODASTR", 75, ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.All(Menu(drawing).Lines, line => Assert.Equal(
            Code128.Svg(line.Barcode.Code, Code128.MenuWidthInches, Code128.MenuHeightInches),
            line.Barcode.Svg));
    }

    // NET-8: the register's own calendar. Movie night's 10:15 PM is already the next day in UTC, and a menu
    // dated tomorrow would look newer than a price changed tonight.
    [Fact]
    public async Task DrawMenuAsync_LateOnMovieNight_IsDatedThatNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(new DateOnly(2026, 10, 23), Menu(drawing).Date);
    }

    // The store's name of record, as on the labels and cards.
    [Fact]
    public async Task DrawMenuAsync_Any_CarriesTheStoreName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        await using (var db = rewind.OpenDb())
        {
            await db.StoreSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.StoreName, "Rewind Video East"), ct);
        }

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal("Rewind Video East", Menu(drawing).StoreName);
    }

    // NET-12: an empty menu would print a heading and a date, and nothing to scan.
    [Fact]
    public async Task DrawMenuAsync_NoSnacks_IsNothingOnSale()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(new MenuDrawing(MenuStatus.NothingOnSale, null), drawing);
    }

    // More snacks than one sheet holds go on to a second as the menu prints, and two laminated sheets are
    // still a menu, so nothing is left off.
    [Fact]
    public async Task DrawMenuAsync_MoreThanOneSheetHolds_PutsEveryOneOnIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnacksAsync(rewind, 14, ct);

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal(14, Menu(drawing).Lines.Count);
    }

    // The menu is the store's whole price list, so its heading carries the rental and the late fee too.
    [Fact]
    public async Task DrawMenuAsync_Any_CarriesTheStorePrices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        await using (var db = rewind.OpenDb())
        {
            await db.StoreSettings.ExecuteUpdateAsync(
                s => s.SetProperty(x => x.RentalPriceCents, 250).SetProperty(x => x.LateFeeCents, 125), ct);
        }

        var drawing = await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal((250, 125), (Menu(drawing).RentalPriceCents, Menu(drawing).LateFeeCents));
    }

    // OBS-3: a printed menu carries its date, so the log says which day's menu is on the counter.
    [Fact]
    public async Task DrawMenuAsync_Drawn_LogsItsSnacksAndDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnacksAsync(rewind, 3, ct);
        var log = new ListLogger<LabelSheets>();

        await Sheets(rewind, log).DrawMenuAsync(ct);

        Assert.Equal(
            [(LogLevel.Information, "Drew the concession menu with 3 snacks, dated 2026-10-23", (Exception?)null)],
            log.Entries);
    }

    // ADR-0006's numbers are for labels and cards: a menu printed twice is the same menu.
    [Fact]
    public async Task DrawMenuAsync_Any_SpendsNoNumbers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);

        await Sheets(rewind).DrawMenuAsync(ct);

        Assert.Equal((1, 1), await NextNumbersAsync(rewind, ct));
    }

    // NET-7: the token goes on to the database, so leaving the Store Manager stops the reads.
    [Fact]
    public async Task DrawMenuAsync_Cancelled_Stops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackAsync(rewind, "Popcorn", "CON-POPCORN", 100, ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var draw = () => Sheets(rewind).DrawMenuAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(draw);
    }

    private static LabelSheets Sheets(TempRewind rewind, ILogger<LabelSheets>? logger = null) =>
        new(rewind.DbFactory, rewind.Time, logger ?? NullLogger<LabelSheets>.Instance);

    private static ConcessionMenu Menu(MenuDrawing drawing) => Assert.IsType<ConcessionMenu>(drawing.Menu);

    // Each line's name, price and code, top to bottom.
    private static List<(string Name, int PriceCents, string Code)> Lines(MenuDrawing drawing) =>
        [.. Menu(drawing).Lines.Select(line => (line.Name, line.PriceCents, line.Barcode.Code))];

    // A snack added before the test, saved straight to the database.
    private static async Task AddSnackAsync(
        TempRewind rewind, string name, string code, int priceCents, CancellationToken ct, bool isActive = true)
    {
        await using var db = rewind.OpenDb();
        db.ConcessionItems.Add(new ConcessionItem { Name = name, Code = code, PriceCents = priceCents, IsActive = isActive });
        await db.SaveChangesAsync(ct);
    }

    // As many snacks on sale as asked for, each with a name and code of its own: CON-SNACKA, CON-SNACKB...
    private static async Task AddSnacksAsync(TempRewind rewind, int count, CancellationToken ct)
    {
        foreach (var letter in Enumerable.Range('A', count).Select(c => (char)c))
        {
            await AddSnackAsync(rewind, $"Snack {letter}", $"CON-SNACK{letter}", 100, ct);
        }
    }

    private static List<string> Codes(SheetReservation reservation) =>
        [.. Assert.IsType<Sheet>(reservation.Sheet).Barcodes.Select(barcode => barcode.Code)];

    private static List<string> StoreLabels(int first, int count) =>
        [.. Enumerable.Range(first, count).Select(number => string.Create(CultureInfo.InvariantCulture, $"RV-{number:D5}"))];

    private static List<string> MemberCards(int first, int count) =>
        [.. Enumerable.Range(first, count).Select(number => string.Create(CultureInfo.InvariantCulture, $"MEM-{number:D3}"))];

    // As only a hand edit in the database could move a counter.
    private static async Task SetNextStoreLabelAsync(TempRewind rewind, int number, CancellationToken ct)
    {
        await using var db = rewind.OpenDb();
        await db.StoreSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.NextStoreLabelNumber, number), ct);
    }

    private static async Task SetNextMemberCardAsync(TempRewind rewind, int number, CancellationToken ct)
    {
        await using var db = rewind.OpenDb();
        await db.StoreSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.NextMemberCardNumber, number), ct);
    }

    private static async Task<(int StoreLabel, int MemberCard)> NextNumbersAsync(TempRewind rewind, CancellationToken ct)
    {
        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        return (settings.NextStoreLabelNumber, settings.NextMemberCardNumber);
    }

    // Contexts that hold the first reservation's save at the gate below.
    private sealed class GatedContexts(TempRewind rewind, FirstSaveWaitsForASecond gate) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(gate)
                .Options);
    }

    // Holds the first save until another context asks to begin a transaction. The gate opens as the
    // second asks, not once it holds one: with BEGIN IMMEDIATE, the second can't begin until the first
    // has committed, so a gate that waited for that would hold each until SQLite's busy timeout. A
    // second that never asks, because the code under test has lost its transaction, opens the gate as
    // it starts to save instead: EF Core saves a single change without a transaction of its own.
    private sealed class FirstSaveWaitsForASecond : DbTransactionInterceptor, ISaveChangesInterceptor
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _first;

        // Completes once the first save is waiting at the gate.
        public Task FirstHeld => _held.Task;

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            var first = Volatile.Read(ref _first);
            if (first is not null && !ReferenceEquals(eventData.Context, first))
            {
                _open.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }

        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _first, eventData.Context, null) is null)
            {
                _held.TrySetResult();
                await _open.Task.WaitAsync(cancellationToken);
            }
            else
            {
                _open.TrySetResult();
            }

            return result;
        }
    }
}
