namespace Rewind.Core.Checkout;

/// <summary>What the screen showed when the clerk typed the cash: the sale to save, as it was rung up (DATA-10).</summary>
/// <param name="MemberId">The member, from the draw.</param>
/// <param name="BusinessDayId">The movie night, from the draw.</param>
/// <param name="Items">The cases on the bill.</param>
/// <param name="Lines">The snacks and the late fee on the bill.</param>
/// <param name="CashTenderedCents">The cash the clerk typed.</param>
public sealed record Ticket(
    int MemberId,
    int BusinessDayId,
    IReadOnlyList<TicketItem> Items,
    IReadOnlyList<TicketLine> Lines,
    int CashTenderedCents)
{
    /// <summary>What the bill comes to, the one sum both the screen and Core use.</summary>
    public int TotalCents => Items.Sum(item => item.PriceCents) + Lines.Sum(line => line.PriceCents);
}
