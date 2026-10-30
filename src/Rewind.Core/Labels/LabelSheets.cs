using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Labels;

/// <summary>
/// Numbers and draws the sheets of store labels and member cards that the Store Manager prints (Build
/// Plan 2.5), and draws the concession menu (Build Plan 3.1). StoreSettings keeps the number each kind's
/// next sheet starts at, and a sheet spends its numbers when it's made, before the print window opens
/// (ADR-0006).
/// </summary>
public sealed partial class LabelSheets(IDbContextFactory<RewindDb> dbFactory, TimeProvider time, ILogger<LabelSheets> logger) : ILabelSheets
{
    /// <summary>The store labels on one sheet: Avery 6460's 30, 3 across and 10 down.</summary>
    public const int LabelsPerSheet = 30;

    /// <summary>The member cards on one sheet: 10, 2 across and 5 down.</summary>
    public const int CardsPerSheet = 10;

    // The last numbers the codes can carry: RV- and five digits, and MEM- and three (Barcodes).
    private const int LastStoreLabelNumber = 99_999;
    private const int LastMemberCardNumber = 999;

    private static readonly Numbering StoreLabels = new(
        SheetKind.StoreLabels,
        "store labels",
        LabelsPerSheet,
        LastStoreLabelNumber,
        StoreLabelCode,
        settings => settings.NextStoreLabelNumber,
        (settings, next) => settings.NextStoreLabelNumber = next,
        Code128.StoreLabelWidthInches,
        Code128.StoreLabelHeightInches);

    private static readonly Numbering MemberCards = new(
        SheetKind.MemberCards,
        "member cards",
        CardsPerSheet,
        LastMemberCardNumber,
        MemberCardCode,
        settings => settings.NextMemberCardNumber,
        (settings, next) => settings.NextMemberCardNumber = next,
        Code128.MemberCardWidthInches,
        Code128.MemberCardHeightInches);

    /// <inheritdoc />
    public async Task<NextCodes> PeekAsync(CancellationToken cancellationToken)
    {
        // DATA-12: read untracked, and only the two numbers. They become codes after the query, not in
        // it, so nothing depends on EF Core running a projection's method calls on the client.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var next = await db.StoreSettings
            .AsNoTracking()
            .Select(s => new { s.NextStoreLabelNumber, s.NextMemberCardNumber })
            .SingleAsync(cancellationToken);
        return new NextCodes(StoreLabelCode(next.NextStoreLabelNumber), MemberCardCode(next.NextMemberCardNumber));
    }

    /// <inheritdoc />
    public Task<SheetReservation> ReserveLabelsAsync(CancellationToken cancellationToken) =>
        ReserveAsync(StoreLabels, cancellationToken);

    /// <inheritdoc />
    public Task<SheetReservation> ReserveCardsAsync(CancellationToken cancellationToken) =>
        ReserveAsync(MemberCards, cancellationToken);

    /// <inheritdoc />
    public async Task<MenuDrawing> DrawMenuAsync(CancellationToken cancellationToken)
    {
        // DATA-12: read untracked, and only what the menu prints. NOCASE, then the code, as the Snacks tab
        // lists them, so the menu reads in the order the Store Manager sees.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var store = await db.StoreSettings
            .AsNoTracking()
            .Select(s => new { s.StoreName, s.RentalPriceCents, s.LateFeeCents })
            .SingleAsync(cancellationToken);
        var onSale = await db.ConcessionItems
            .AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => EF.Functions.Collate(i.Name, "NOCASE"))
            .ThenBy(i => i.Code)
            .Select(i => new { i.Name, i.Code, i.PriceCents })
            .ToListAsync(cancellationToken);

        if (onSale.Count == 0)
        {
            return new MenuDrawing(MenuStatus.NothingOnSale, null);
        }

        // NET-8: today on the register's own calendar, for the footer that says how old a printed menu is.
        var date = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var menu = new ConcessionMenu(
            store.StoreName,
            store.RentalPriceCents,
            store.LateFeeCents,
            date,
            [.. onSale.Select(item => new MenuLine(
                item.Name,
                item.PriceCents,
                new SheetBarcode(item.Code, Code128.Svg(item.Code, Code128.MenuWidthInches, Code128.MenuHeightInches))))]);
        LogMenuDrawn(logger, menu.Lines.Count, date);
        return new MenuDrawing(MenuStatus.Drawn, menu);
    }

    // ADR-0006: the counter is read, and the sheet's numbers spent, in one transaction (DATA-7). EF Core
    // begins it IMMEDIATE, which takes SQLite's write lock before the read, so a second sheet made at
    // the same time waits until this one has committed, then reads the advanced counter. That's what
    // keeps two sheets from ever sharing a number; the screen's busy guard only spares the clerk a
    // second print window.
    private async Task<SheetReservation> ReserveAsync(Numbering numbering, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.StoreSettings.SingleAsync(cancellationToken);
        var first = numbering.Next(settings);
        var last = first + numbering.PerSheet - 1;

        // Sheets are only ever full, so the last few numbers before the limit are never printed.
        if (last > numbering.LastNumber)
        {
            LogOutOfNumbers(logger, numbering.Name, first, numbering.LastNumber);
            return new SheetReservation(SheetStatus.OutOfNumbers, null);
        }

        // Drawn before the numbers are spent, so a sheet that couldn't be drawn spends nothing.
        var sheet = Draw(numbering, settings.StoreName, first);
        numbering.SetNext(settings, last + 1);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogReserved(logger, sheet.First, sheet.Last, numbering.Name);
        return new SheetReservation(SheetStatus.Reserved, sheet);
    }

    private static Sheet Draw(Numbering numbering, string storeName, int first) =>
        new(
            numbering.Kind,
            storeName,
            [.. Enumerable.Range(first, numbering.PerSheet).Select(number =>
            {
                var code = numbering.Code(number);
                return new SheetBarcode(code, Code128.Svg(code, numbering.BarcodeWidthInches, numbering.BarcodeHeightInches));
            })]);

    // In the forms Barcodes reads: RV- and five digits, and MEM- and three.
    private static string StoreLabelCode(int number) => string.Create(CultureInfo.InvariantCulture, $"RV-{number:00000}");

    private static string MemberCardCode(int number) => string.Create(CultureInfo.InvariantCulture, $"MEM-{number:000}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Reserved {First} to {Last} for a sheet of {Sheet}")]
    private static partial void LogReserved(ILogger logger, string first, string last, string sheet);

    // Warning, not Error (OBS-3): the Store Manager is told, and nothing is spent.
    [LoggerMessage(Level = LogLevel.Warning, Message = "No numbers are left for a sheet of {Sheet}: the next would start at {First}, and the last is {LastNumber}")]
    private static partial void LogOutOfNumbers(ILogger logger, string sheet, int first, int lastNumber);

    // OBS-3: a printed menu is dated, so the day it was drawn is worth finding later, such as when a price
    // has changed since.
    [LoggerMessage(Level = LogLevel.Information, Message = "Drew the concession menu with {Count} snacks, dated {Date:yyyy-MM-dd}")]
    private static partial void LogMenuDrawn(ILogger logger, int count, DateOnly date);

    // How one kind of sheet is numbered and drawn: its name in the log, how many codes a sheet holds,
    // the last number its codes can carry, how a number becomes a code, its counter in StoreSettings,
    // and the printed size of its barcodes.
    private sealed record Numbering(
        SheetKind Kind,
        string Name,
        int PerSheet,
        int LastNumber,
        Func<int, string> Code,
        Func<StoreSettings, int> Next,
        Action<StoreSettings, int> SetNext,
        double BarcodeWidthInches,
        double BarcodeHeightInches);
}
