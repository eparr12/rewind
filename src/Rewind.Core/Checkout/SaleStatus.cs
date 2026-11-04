namespace Rewind.Core.Checkout;

/// <summary>Whether a sale completed, and if not, what to fix (NET-12).</summary>
public enum SaleStatus
{
    /// <summary>The sale is saved.</summary>
    Completed,

    /// <summary>The ticket has nothing on it, and nothing is saved.</summary>
    EmptyTicket,

    /// <summary>The cash is under the total, and nothing is saved.</summary>
    CashShort,

    /// <summary>A case on the ticket is no longer on the shelf, and nothing is saved.</summary>
    NotOnShelf,
}
