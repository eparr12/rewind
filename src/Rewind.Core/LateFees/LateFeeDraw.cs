namespace Rewind.Core.LateFees;

/// <summary>A member's draw from their late-fee bag on one movie night (<see cref="ShuffleBag"/>).</summary>
public sealed class LateFeeDraw
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The member who drew.</summary>
    public int MemberId { get; set; }

    /// <summary>The movie night it was drawn on. A member draws once a night (DATA-11).</summary>
    public int BusinessDayId { get; set; }

    /// <summary>The card the member drew.</summary>
    public Card Card { get; set; }

    /// <summary>
    /// The movie an Overdue card's alert names. Null for a Clear card, or when the collection has no movies.
    /// </summary>
    public int? FlavorMovieId { get; set; }

    /// <summary>When it was drawn, in UTC (DATA-8).</summary>
    public DateTime DrawnAt { get; set; }
}
