namespace Rewind.Core.Labels;

/// <summary>
/// The concession menu, drawn and ready to print: the store's prices, the rental and the late fee, then every
/// snack on sale, each with its price and the barcode the clerk scans at Checkout (Build Plan 3.1). The menu
/// is laminated, so it carries the day it was drawn, and a price changed after that day means printing it
/// again.
/// </summary>
/// <param name="StoreName">The store's name, from StoreSettings, for the menu's heading.</param>
/// <param name="RentalPriceCents">What a movie rents for, in cents, from StoreSettings, for the heading.</param>
/// <param name="LateFeeCents">The late fee, in cents, from StoreSettings, for the heading.</param>
/// <param name="Date">The day it was drawn, on the register's calendar, for the menu's footer.</param>
/// <param name="Lines">
/// Each snack on sale, in order of name, then code, as the Snacks tab lists them. However many there are,
/// every one is here: a long menu goes on to a second sheet as it prints.
/// </param>
public sealed record ConcessionMenu(string StoreName, int RentalPriceCents, int LateFeeCents, DateOnly Date, IReadOnlyList<MenuLine> Lines);
