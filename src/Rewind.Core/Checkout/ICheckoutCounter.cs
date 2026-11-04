using Rewind.Core.LateFees;

namespace Rewind.Core.Checkout;

/// <summary>
/// What F1 Checkout asks of Core, one call per thing the clerk does (ARCH-5): a member's card, a case's label
/// and the cash. The screen keeps the open sale in its own fields (ADR-0003), and nothing about the sale is
/// saved until <see cref="CompleteAsync"/>. Snacks need no call here, since the concession bar answers for
/// them. <see cref="CheckoutCounter"/> does it, and the UI's tests use a fake.
/// </summary>
public interface ICheckoutCounter
{
    /// <summary>
    /// Starts a sale for the member whose card this is. The card is read as a scan is, so mem-001 is MEM-001.
    /// A card nobody has, or a deactivated member's, comes back as that status, and nothing is drawn.
    /// Otherwise the member gets their card for the night (<see cref="ILateFeeDraws"/>), which stands whether
    /// or not the sale completes. An Overdue card whose fee isn't paid yet comes with the fee to put on the
    /// bill, at the late fee as it is now (ADR-0010).
    /// </summary>
    Task<SaleStart> StartAsync(string cardBarcode, CancellationToken cancellationToken);

    /// <summary>
    /// The case with this store label, with its movies, to go on the bill. The label is read as a scan is, so
    /// rv-00042 is RV-00042, and a barcode that isn't a store label is no case's. A case that's checked out or
    /// retired comes back <see cref="CaseScanStatus.NotOnShelf"/>.
    /// </summary>
    Task<CaseScan> ScanCaseAsync(string storeLabel, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a sale: saves the rental, its items and lines, and each case's new status, all or nothing
    /// (DATA-7). An empty ticket, cash under the total, or a case that's no longer on the shelf comes back as
    /// that status, and nothing is saved. A second fee line for one draw throws: the unique index refuses it,
    /// and only a second register or a bug could send one (ADR-0010).
    /// </summary>
    Task<SaleResult> CompleteAsync(Ticket ticket, CancellationToken cancellationToken);
}
