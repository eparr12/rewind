namespace Rewind.Core.Checkout;

/// <summary>
/// One completed checkout: who, on which movie night, and the money. Rentals are due back the night they're
/// rented, so <see cref="BusinessDayId"/> is the due date too. Only a sale that completes is saved, in one
/// transaction with its items and lines (DATA-7), and nothing here is ever deleted (DATA-4).
/// </summary>
public sealed class Rental
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The member who rented.</summary>
    public int MemberId { get; set; }

    /// <summary>The movie night it was rented on, and due back on.</summary>
    public int BusinessDayId { get; set; }

    /// <summary>When the sale completed, in UTC (DATA-8).</summary>
    public DateTime RentedAt { get; set; }

    /// <summary>What the items and lines came to, in cents (DATA-2).</summary>
    public int TotalCents { get; set; }

    /// <summary>The cash the clerk typed, in cents. It's never less than the total.</summary>
    public int CashTenderedCents { get; set; }

    /// <summary>The change given, in cents: always the cash less the total.</summary>
    public int ChangeCents { get; set; }

    /// <summary>The cases rented, each with the movie picked from it.</summary>
    public ICollection<RentalItem> Items { get; } = [];

    /// <summary>The snacks and the late fee on the same bill.</summary>
    public ICollection<SaleLine> Lines { get; } = [];
}
