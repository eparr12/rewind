using Rewind.Core.Money;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Concessions;

/// <summary>
/// What the Store Manager asks of Core to keep the concession bar's snacks and drinks, one call per action
/// (ARCH-5): the list, and adding, renaming, repricing, deactivating and reactivating an item. Nothing
/// deletes an item (DATA-4), and each code belongs to one item (DATA-11). The status bar asks it which item
/// a scanned code is. <see cref="ConcessionBar"/> does it, and the UI's tests use a fake (ADR-0003).
/// </summary>
public interface IConcessionBar
{
    /// <summary>Every item, active or not, in order of name, then code.</summary>
    Task<IReadOnlyList<ConcessionRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The item with this code, active or not, or null when no item has it. The code is read as a scan is,
    /// so con-popcorn is CON-POPCORN, and a barcode that isn't a concession code is no item's.
    /// </summary>
    Task<ConcessionRow?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an active item, checking the name, then the code, then the price, as the Store Manager fills them
    /// in. The name is cleaned as a member's is, and cut at <see cref="ConcessionItem.MaxNameLength"/>
    /// characters. The code is read as a scan is, so con-popcorn is CON-POPCORN, and must be one Barcodes
    /// reads as a concession. The price is typed in dollars, as <see cref="Dollars.TryParse"/> reads it, up to
    /// <see cref="StoreSettings.MaxPriceCents"/>. A code another item has, active or not, comes back
    /// <see cref="ConcessionChangeStatus.CodeInUse"/>, with that item.
    /// </summary>
    Task<ConcessionChange> AddAsync(string name, string code, string price, CancellationToken cancellationToken);

    /// <summary>
    /// Fixes an item's name, cleaned as <see cref="AddAsync"/> cleans it. Renaming is for typos: a different
    /// snack is a new item, with a code of its own, and the old one is deactivated. A name that's the same
    /// once cleaned changes nothing, and nothing is logged.
    /// </summary>
    Task<ConcessionChange> RenameAsync(int itemId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Changes an item's price, typed in dollars, as for <see cref="AddAsync"/>. Each sale copies the price it
    /// charged (DATA-10), so a change counts from the next sale on. A price that's the same as saved changes
    /// nothing, and nothing is logged.
    /// </summary>
    Task<ConcessionChange> SetPriceAsync(int itemId, string price, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates an item, or reactivates one. A deactivated item keeps its code and its history (DATA-4).
    /// Asking for the state an item is in already changes nothing, and nothing is logged.
    /// </summary>
    Task<ConcessionChange> SetActiveAsync(int itemId, bool isActive, CancellationToken cancellationToken);
}
