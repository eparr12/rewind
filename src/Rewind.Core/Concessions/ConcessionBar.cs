using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.Metadata;
using Rewind.Core.Money;
using Rewind.Core.Scans;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Concessions;

/// <summary>
/// Keeps the concession bar's snacks and drinks for the Store Manager (Build Plan 3.1): each with a name, the
/// code on the concession menu and a price, deactivated when it's no longer sold, never deleted (DATA-4).
/// </summary>
public sealed partial class ConcessionBar(IDbContextFactory<RewindDb> dbFactory, ILogger<ConcessionBar> logger) : IConcessionBar
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConcessionRow>> ListAsync(CancellationToken cancellationToken)
    {
        // DATA-12: read untracked, straight into rows. NOCASE, so a name typed in lower case sorts among
        // the others.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ConcessionItems
            .AsNoTracking()
            .OrderBy(i => EF.Functions.Collate(i.Name, "NOCASE"))
            .ThenBy(i => i.Code)
            .Select(i => new ConcessionRow(i.Id, i.Name, i.Code, i.PriceCents, i.IsActive))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConcessionRow?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        var scan = Barcodes.Classify(code);
        if (scan.Kind != ScanKind.Concession)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await FindByCodeAsync(db, scan.Code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConcessionChange> AddAsync(string name, string code, string price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(price);

        if (Clean(name) is not { } cleanName)
        {
            return new ConcessionChange(ConcessionChangeStatus.NoName, null);
        }

        var scan = Barcodes.Classify(code);
        if (scan.Kind != ScanKind.Concession)
        {
            return new ConcessionChange(ConcessionChangeStatus.NotACode, null);
        }

        if (Read(price) is not { } priceCents)
        {
            return new ConcessionChange(ConcessionChangeStatus.NotAPrice, null);
        }

        try
        {
            return await SaveNewAsync(cleanName, scan.Code, priceCents, cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: the code was free when SaveNewAsync looked, and another item took it before the save.
            // The unique index is the real guard. Anything else it refused would be a bug.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindByCodeAsync(db, scan.Code, cancellationToken) is { } owner)
            {
                return CodeInUse(owner);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConcessionChange> RenameAsync(int itemId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Clean(name) is not { } cleanName)
        {
            return new ConcessionChange(ConcessionChangeStatus.NoName, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await FindAsync(db, itemId, cancellationToken);
        if (item.Name == cleanName)
        {
            return Saved(item); // OBS-3: the log is an item's only history, so no line for no change
        }

        var oldName = item.Name;
        item.Name = cleanName;
        await db.SaveChangesAsync(cancellationToken);
        LogRenamed(logger, item.Code, oldName, cleanName);
        return Saved(item);
    }

    /// <inheritdoc />
    public async Task<ConcessionChange> SetPriceAsync(int itemId, string price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(price);

        if (Read(price) is not { } priceCents)
        {
            return new ConcessionChange(ConcessionChangeStatus.NotAPrice, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await FindAsync(db, itemId, cancellationToken);
        if (item.PriceCents == priceCents)
        {
            return Saved(item); // as for a rename, no line for no change
        }

        var oldPriceCents = item.PriceCents;
        item.PriceCents = priceCents;
        await db.SaveChangesAsync(cancellationToken);

        // In dollars, for the family reading the log, and formatted only for a line that's written (CA1873).
        if (logger.IsEnabled(LogLevel.Information))
        {
            var oldPrice = Dollars.Format(oldPriceCents);
            var newPrice = Dollars.Format(priceCents);
            LogRepriced(logger, item.Code, item.Name, oldPrice, newPrice);
        }

        return Saved(item);
    }

    /// <inheritdoc />
    public async Task<ConcessionChange> SetActiveAsync(int itemId, bool isActive, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await FindAsync(db, itemId, cancellationToken);
        if (item.IsActive == isActive)
        {
            return Saved(item); // as for a rename, no line for no change
        }

        item.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        LogActiveChanged(logger, item.Code, item.Name, isActive ? "active" : "inactive");
        return Saved(item);
    }

    private async Task<ConcessionChange> SaveNewAsync(string name, string code, int priceCents, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindByCodeAsync(db, code, cancellationToken) is { } owner)
        {
            return CodeInUse(owner);
        }

        var item = new ConcessionItem { Name = name, Code = code, PriceCents = priceCents };
        db.ConcessionItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var price = Dollars.Format(priceCents);
            LogAdded(logger, code, name, price);
        }

        return Saved(item);
    }

    private ConcessionChange CodeInUse(ConcessionRow owner)
    {
        LogCodeInUse(logger, owner.Code, owner.Name);
        return new ConcessionChange(ConcessionChangeStatus.CodeInUse, owner);
    }

    // Typed by the Store Manager, so it's cleaned as a member's name is (SEC-5).
    private static string? Clean(string name) => WebAnswers.Clean(name, ConcessionItem.MaxNameLength);

    // Typed by the Store Manager, as the rental price is, and to the same limit.
    private static int? Read(string typed) =>
        Dollars.TryParse(typed, out var cents) && cents <= StoreSettings.MaxPriceCents ? cents : null;

    // Items are never deleted (DATA-4), so an Id from the Store Manager's list is always there.
    private static async Task<ConcessionItem> FindAsync(RewindDb db, int itemId, CancellationToken cancellationToken) =>
        await db.ConcessionItems.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken)
        ?? throw new InvalidOperationException($"There's no concession item {itemId}, though items are never deleted.");

    private static Task<ConcessionRow?> FindByCodeAsync(RewindDb db, string code, CancellationToken cancellationToken) =>
        db.ConcessionItems
            .AsNoTracking()
            .Where(i => i.Code == code)
            .Select(i => new ConcessionRow(i.Id, i.Name, i.Code, i.PriceCents, i.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    private static ConcessionChange Saved(ConcessionItem item) =>
        new(ConcessionChangeStatus.Saved, new ConcessionRow(item.Id, item.Name, item.Code, item.PriceCents, item.IsActive));

    [LoggerMessage(Level = LogLevel.Information, Message = "Added snack {Code}, {Name}, at ${Price}")]
    private static partial void LogAdded(ILogger logger, string code, string name, string price);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed snack {Code} from {OldName} to {NewName}")]
    private static partial void LogRenamed(ILogger logger, string code, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Snack {Code}, {Name}, changed from ${OldPrice} to ${NewPrice}")]
    private static partial void LogRepriced(ILogger logger, string code, string name, string oldPrice, string newPrice);

    [LoggerMessage(Level = LogLevel.Information, Message = "Snack {Code}, {Name}, is {State}")]
    private static partial void LogActiveChanged(ILogger logger, string code, string name, string state);

    // Information, not Debug (OBS-3): a code that's another snack's is worth finding in the log later.
    [LoggerMessage(Level = LogLevel.Information, Message = "Snack code {Code} is {Name}'s already, so nothing was saved")]
    private static partial void LogCodeInUse(ILogger logger, string code, string name);
}
