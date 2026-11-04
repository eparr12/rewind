namespace Rewind.Core.Checkout;

/// <summary>A snack or the late fee on the bill, as it was rung up.</summary>
/// <param name="Name">The name it goes by on the bill.</param>
/// <param name="PriceCents">The price it was rung up at.</param>
/// <param name="ConcessionItemId">The snack. Null on a fee line.</param>
/// <param name="LateFeeDrawId">The draw whose fee this pays. Null on a snack's line.</param>
public sealed record TicketLine(string Name, int PriceCents, int? ConcessionItemId, int? LateFeeDrawId);
