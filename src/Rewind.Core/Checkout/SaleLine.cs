using Rewind.Core.Metadata;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>
/// One line on a rental's bill that isn't a case: a snack, or the late fee for an Overdue draw. Its name and
/// price are copied at the sale, so it reads as it was rung up whatever changes later (DATA-10). A fee line
/// is also what says a draw's fee is paid (ADR-0010).
/// </summary>
public sealed class SaleLine
{
    /// <summary>
    /// The longest name a line can have: room for a late fee naming a movie with the longest title Rewind
    /// keeps, with "Late fee: " and " (1978)" around it.
    /// </summary>
    public const int MaxNameLength = TmdbClient.MaxNameLength + 17;

    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The rental it's on.</summary>
    public int RentalId { get; set; }

    /// <summary>The name as it was rung up, such as Popcorn or Late fee: Halloween (1978).</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The price as it was rung up, in cents, from 0 to <see cref="StoreSettings.MaxPriceCents"/>.
    /// </summary>
    public int PriceCents { get; set; }

    /// <summary>The snack sold. Null on a fee line: a line has this or <see cref="LateFeeDrawId"/>, never both.</summary>
    public int? ConcessionItemId { get; set; }

    /// <summary>The draw whose fee this pays. Null on a snack's line. A draw's fee is paid once (ADR-0010).</summary>
    public int? LateFeeDrawId { get; set; }
}
