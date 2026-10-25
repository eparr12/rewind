namespace Rewind.UI;

/// <summary>How the screens name a movie in a line of text.</summary>
internal static class MovieTitle
{
    /// <summary>
    /// The title and the year it came out, such as "Halloween (1978)", since remakes share titles.
    /// A movie whose year isn't known (DATA-3) has its title alone.
    /// </summary>
    public static string WithYear(string title, int? year) => year is null ? title : $"{title} ({year})";
}
