using Rewind.Core.StoreManager;

namespace Rewind.Core.Checkout;

/// <summary>One case on a rental, with the movie the member is taking home from it.</summary>
public sealed class RentalItem
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The rental it's on.</summary>
    public int RentalId { get; set; }

    /// <summary>The case rented. A case rents once per sale (DATA-11).</summary>
    public int CaseId { get; set; }

    /// <summary>The movie picked from the case, which must be one the case holds (DATA-11).</summary>
    public int MovieId { get; set; }

    /// <summary>
    /// The rental price as it was rung up, in cents, from 0 to <see cref="StoreSettings.MaxPriceCents"/>.
    /// A later price change doesn't touch it (DATA-10).
    /// </summary>
    public int PriceCents { get; set; }

    /// <summary>
    /// When the case came back, in UTC (DATA-8), or null while it's out. A case is out on one rental at a
    /// time (DATA-11). A return belongs to no business day, so this time is all it keeps.
    /// </summary>
    public DateTime? ReturnedAt { get; set; }
}
