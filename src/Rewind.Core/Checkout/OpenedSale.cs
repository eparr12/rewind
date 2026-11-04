using Rewind.Core.LateFees;

namespace Rewind.Core.Checkout;

/// <summary>What an open sale starts from.</summary>
/// <param name="MemberName">The member's name, as the screen shows it.</param>
/// <param name="Draw">The member's card for the night, which gives the ticket its member and movie night.</param>
/// <param name="RentalPriceCents">The rental price now, which each case goes on the bill at.</param>
/// <param name="Fee">The late fee to put on the bill. Null for a Clear card, or an Overdue card whose fee is paid.</param>
public sealed record OpenedSale(string MemberName, LateFeeDrawRow Draw, int RentalPriceCents, FeeDue? Fee);
