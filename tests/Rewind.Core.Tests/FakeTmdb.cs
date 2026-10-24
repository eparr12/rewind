using System.Globalization;

namespace Rewind.Core.Tests;

/// <summary>TMDB's API and image server, for tests: a <see cref="FakeWebApi"/> with TMDB's addresses.</summary>
internal sealed class FakeTmdb : FakeWebApi
{
    /// <summary>Where TmdbClient should search for a title, already escaped as a URL's query.</summary>
    public static string SearchFor(string escapedTitle) => $"/3/search/movie?query={escapedTitle}&include_adult=false";

    /// <summary>Where TmdbClient should ask for a movie's details.</summary>
    public static string MovieFor(int tmdbId) =>
        string.Create(CultureInfo.InvariantCulture, $"/3/movie/{tmdbId}?append_to_response=credits,release_dates");

    /// <summary>Where TmdbClient should download a poster at a width, such as w342.</summary>
    public static string PosterFor(string width, string posterPath) => $"/t/p/{width}{posterPath}";
}
