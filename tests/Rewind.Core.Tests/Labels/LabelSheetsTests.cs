using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var sheets = new LabelSheets(new GatedContexts(rewind, gate), NullLogger<LabelSheets>.Instance);

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

    private static LabelSheets Sheets(TempRewind rewind, ILogger<LabelSheets>? logger = null) =>
        new(rewind.DbFactory, logger ?? NullLogger<LabelSheets>.Instance);

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
