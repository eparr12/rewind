namespace Rewind.Core.Returns;

/// <summary>
/// One row of the watch log: a member who watched a rented movie, with their stars. A rental item's rows are
/// who the clerk said watched it, so an item with none was skipped or never asked about, and nothing says
/// which. A viewing has no time of its own: the movie was watched on its rental's night. Nothing here is ever
/// deleted (DATA-4).
/// </summary>
public sealed class Viewing
{
    /// <summary>The fewest stars a member can give a movie.</summary>
    public const int MinStars = 1;

    /// <summary>The most stars a member can give a movie.</summary>
    public const int MaxStars = 5;

    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The rental item watched: the case, and the movie picked from it.</summary>
    public int RentalItemId { get; set; }

    /// <summary>The member who watched. A member is on a rental item's watch log once (DATA-11).</summary>
    public int MemberId { get; set; }

    /// <summary>
    /// The member's stars, from <see cref="MinStars"/> to <see cref="MaxStars"/>, or null when they skipped
    /// rating it (DATA-3).
    /// </summary>
    public int? Stars { get; set; }
}
