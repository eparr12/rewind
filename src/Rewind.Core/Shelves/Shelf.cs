using Rewind.Core.Members;

namespace Rewind.Core.Shelves;

/// <summary>
/// A shelf of movies, of one of two kinds, told apart by its member (ADR-0015). A shelf with a member is that
/// member's Staff Picks: every member has one from the save that adds them, with no name, season or deactivation of
/// its own, since it goes by its member's. A shelf without one is seasonal, such as Halloween: the Store Manager's,
/// with a name and a season that runs from one day of the year to another. The database holds each kind to its own
/// columns (DATA-11). Seasonal shelves are deactivated, never deleted (DATA-4), and a TMDB refresh writes to no
/// shelf (DATA-9).
/// </summary>
public sealed class Shelf
{
    /// <summary>
    /// The longest name a seasonal shelf can have: room for a name such as Christmas Classics, not a sentence. It's
    /// Core's cap, as <see cref="Member.MaxNameLength"/> is, not the database's.
    /// </summary>
    public const int MaxNameLength = 30;

    /// <summary>January, the first month a season can start or end in.</summary>
    public const int FirstMonth = 1;

    /// <summary>December, the last month a season can start or end in.</summary>
    public const int LastMonth = 12;

    /// <summary>The first day of every month.</summary>
    public const int FirstDay = 1;

    // A season has no year, so its days are a leap year's, and February 29 is one of them (ADR-0015).
    private const int ALeapYear = 2024;

    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The member whose Staff Picks this is, or null for a seasonal shelf. A member has one (DATA-11).</summary>
    public int? MemberId { get; set; }

    /// <summary>
    /// A seasonal shelf's name, such as Halloween, no longer than <see cref="MaxNameLength"/>. No two seasonal shelves
    /// share one, whatever its letter case, which SQLite folds for A to Z only (#100). Null for a member's shelf,
    /// which is named for its member, so it follows a rename.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The month a seasonal shelf's season starts in, from <see cref="FirstMonth"/> to <see cref="LastMonth"/>, or null
    /// for a member's shelf. A season has no year: it comes round every year.
    /// </summary>
    public int? FromMonth { get; set; }

    /// <summary>
    /// The day of <see cref="FromMonth"/> the season starts on, from <see cref="FirstDay"/> to the month's last, as
    /// <see cref="LastDayOf"/> gives it, or null for a member's shelf.
    /// </summary>
    public int? FromDay { get; set; }

    /// <summary>
    /// The month the season ends in, as <see cref="FromMonth"/> is a month, or null for a member's shelf. It can come
    /// before <see cref="FromMonth"/>, for a season that runs over New Year.
    /// </summary>
    public int? ToMonth { get; set; }

    /// <summary>
    /// The day of <see cref="ToMonth"/> the season ends on, as <see cref="FromDay"/> is a day, or null for a member's
    /// shelf. The season takes in both its days.
    /// </summary>
    public int? ToDay { get; set; }

    /// <summary>
    /// False once the Store Manager deactivates a seasonal shelf. A member's shelf is always active, since it goes by
    /// its member: it's hidden while they're deactivated, and back when they are (ADR-0015).
    /// </summary>
    public bool IsActive { get; set; } = true;

    // The only navigation in this folder: MemberRoster saves a new member and their shelf in one SaveChanges
    // (DATA-7), and only a navigation can link the shelf to the member before the member has its key.

    /// <summary>The member whose Staff Picks this is, for saving the two together.</summary>
    public Member? Member { get; set; }

    /// <summary>
    /// The last day a season can start or end on in a month from <see cref="FirstMonth"/> to <see cref="LastMonth"/>:
    /// the month's last day, and 29 for February, since a season has no year.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The month is before <see cref="FirstMonth"/> or after <see cref="LastMonth"/>.</exception>
    public static int LastDayOf(int month) => DateTime.DaysInMonth(ALeapYear, month);
}
