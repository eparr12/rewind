using Rewind.Core.Catalog;

namespace Rewind.UI;

/// <summary>How the screens name a movie in a line of text.</summary>
internal static class MovieTitle
{
    /// <summary>The title and the year, as <see cref="Movie.TitleWithYear"/> writes them.</summary>
    public static string WithYear(string title, int? year) => Movie.TitleWithYear(title, year);
}
