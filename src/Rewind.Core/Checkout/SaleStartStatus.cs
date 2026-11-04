namespace Rewind.Core.Checkout;

/// <summary>Whether a member's card started a sale, and if not, why (NET-12).</summary>
public enum SaleStartStatus
{
    /// <summary>The sale is open, and the member has their card for the night.</summary>
    Started,

    /// <summary>No member has this card, and nothing is drawn.</summary>
    NoSuchMember,

    /// <summary>The card's member has been deactivated, and nothing is drawn.</summary>
    MemberInactive,
}
