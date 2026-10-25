namespace Rewind.Core.Catalog;

/// <summary>
/// The US ratings Rewind keeps for a movie, from TMDB or typed by hand. Anything else, such as
/// TMDB's NR ("No rating information") or a TV rating, is unknown, and so NULL (DATA-3). Movies'
/// CHECK on Rating is built from <see cref="All"/>, so the two are changed together, with a
/// migration (see <see cref="MovieConfiguration"/>).
/// </summary>
public static class UsRatings
{
    /// <summary>The ratings, mildest first: G, PG, PG-13, R and NC-17.</summary>
    public static IReadOnlyList<string> All { get; } = ["G", "PG", "PG-13", "R", "NC-17"];

    /// <summary>
    /// The rating some text names, in its usual form, such as PG for " pg ". Spaces around it and
    /// letter case don't matter. Null when the text is null or names none of <see cref="All"/>.
    /// </summary>
    public static string? Find(string? text)
    {
        var trimmed = text?.Trim();
        return All.FirstOrDefault(rating => string.Equals(rating, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
