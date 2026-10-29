using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.Money;

namespace Rewind.Core.StoreManager;

/// <summary>
/// Keeps the rental price and the late fee for the Store Manager (Build Plan 3.1), in StoreSettings'
/// one row. Each sale copies the price it used (DATA-10), so a change counts from the next sale on.
/// </summary>
public sealed partial class StorePrices(IDbContextFactory<RewindDb> dbFactory, ILogger<StorePrices> logger) : IStorePrices
{
    /// <inheritdoc />
    public async Task<Prices> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StoreSettings
            .AsNoTracking()
            .Select(s => new Prices(s.RentalPriceCents, s.LateFeeCents))
            .SingleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PriceChange> SaveAsync(string rentalPrice, string lateFee, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rentalPrice);
        ArgumentNullException.ThrowIfNull(lateFee);

        if (Read(rentalPrice) is not { } rentalPriceCents)
        {
            return new PriceChange(PriceChangeStatus.RentalPriceNotValid, null);
        }

        if (Read(lateFee) is not { } lateFeeCents)
        {
            return new PriceChange(PriceChangeStatus.LateFeeNotValid, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.StoreSettings.SingleAsync(cancellationToken);
        var was = new Prices(settings.RentalPriceCents, settings.LateFeeCents);
        settings.RentalPriceCents = rentalPriceCents;
        settings.LateFeeCents = lateFeeCents;
        await db.SaveChangesAsync(cancellationToken); // EF Core writes only what changed, so nothing when neither did

        // A line for each price that changed, and none for a price saved as it was, as for a member's
        // rename. Each is written in dollars, for the family reading the log, and formatted only for a
        // line that's written (CA1873).
        if (rentalPriceCents != was.RentalPriceCents && logger.IsEnabled(LogLevel.Information))
        {
            var oldPrice = Dollars.Format(was.RentalPriceCents);
            var newPrice = Dollars.Format(rentalPriceCents);
            LogRentalPriceChanged(logger, oldPrice, newPrice);
        }

        if (lateFeeCents != was.LateFeeCents && logger.IsEnabled(LogLevel.Information))
        {
            var oldPrice = Dollars.Format(was.LateFeeCents);
            var newPrice = Dollars.Format(lateFeeCents);
            LogLateFeeChanged(logger, oldPrice, newPrice);
        }

        return new PriceChange(PriceChangeStatus.Saved, new Prices(rentalPriceCents, lateFeeCents));
    }

    // Typed by the Store Manager, so a price past the limit, such as 200 typed for 2.00, is refused.
    private static int? Read(string typed) =>
        Dollars.TryParse(typed, out var cents) && cents <= StoreSettings.MaxPriceCents ? cents : null;

    // Information (OBS-3): every sale after a change charges the new price, so the change is worth
    // finding in the log later.
    [LoggerMessage(Level = LogLevel.Information, Message = "Rental price changed from ${OldPrice} to ${NewPrice}")]
    private static partial void LogRentalPriceChanged(ILogger logger, string oldPrice, string newPrice);

    [LoggerMessage(Level = LogLevel.Information, Message = "Late fee changed from ${OldPrice} to ${NewPrice}")]
    private static partial void LogLateFeeChanged(ILogger logger, string oldPrice, string newPrice);
}
