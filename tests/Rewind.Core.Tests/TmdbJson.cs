using System.Text.Json;

namespace Rewind.Core.Tests;

/// <summary>
/// TMDB's answers, written out by hand in TMDB's own shape, so a test fails if the client reads a
/// name TMDB doesn't send, such as iso31661 for iso_3166_1. They carry Halloween (1978)'s real
/// facts, checked against TMDB on October 24, 2026, plus a few fields Rewind ignores. The stories
/// are made up.
/// </summary>
internal static class TmdbJson
{
    public const int HalloweenId = 948;
    public const string HalloweenPoster = "/wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg";
    public const int Halloween2018Id = 424139;
    public const int HalloweenStoreId = 1446523;

    // TMDB's IDs for Halloween (1978)'s genres.
    public const int HorrorId = 27;
    public const int ThrillerId = 53;

    /// <summary>
    /// A search for Halloween: the 1978 original, the 2018 sequel, and Halloween Store, which TMDB
    /// sends with an empty release date and, here, no poster.
    /// </summary>
    public const string HalloweenSearch = """
        {
          "page": 1,
          "results": [
            {
              "adult": false,
              "genre_ids": [27, 53],
              "id": 948,
              "original_language": "en",
              "original_title": "Halloween",
              "overview": "A masked killer comes home to Haddonfield on Halloween night.",
              "popularity": 12.5,
              "poster_path": "/wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg",
              "release_date": "1978-10-25",
              "title": "Halloween",
              "video": false,
              "vote_average": 7.5,
              "vote_count": 6000
            },
            {
              "adult": false,
              "genre_ids": [27, 53],
              "id": 424139,
              "original_language": "en",
              "original_title": "Halloween",
              "overview": "Forty years on, Laurie Strode is ready for him.",
              "popularity": 10.1,
              "poster_path": "/f7JAX5EGk4GgsEnus6OxyzwpFp7.jpg",
              "release_date": "2018-10-18",
              "title": "Halloween",
              "video": false,
              "vote_average": 6.5,
              "vote_count": 5000
            },
            {
              "adult": false,
              "genre_ids": [27, 35],
              "id": 1446523,
              "original_language": "en",
              "original_title": "Halloween Store",
              "overview": "",
              "popularity": 1.2,
              "poster_path": null,
              "release_date": "",
              "title": "Halloween Store",
              "video": false,
              "vote_average": 0,
              "vote_count": 0
            }
          ],
          "total_pages": 37,
          "total_results": 728
        }
        """;

    /// <summary>
    /// A movie's details with its credits and release dates, as one call returns them. Each part
    /// defaults to Halloween (1978): 91 minutes, rated R in the US, directed by John Carpenter.
    /// </summary>
    public static string Movie(
        string? title = "Halloween",
        string? releaseDate = "1978-10-25",
        int? runtime = 91,
        string? overview = "A masked killer comes home to Haddonfield on Halloween night.",
        string? posterPath = HalloweenPoster,
        (string? Name, int Order)[]? cast = null,
        (string? Name, string Job)[]? crew = null,
        (string Country, string? Certification)[]? releases = null,
        (int Id, string? Name)[]? genres = null) =>
        $$"""
        {
          "adult": false,
          "budget": 325000,
          "genres": [{{string.Join(", ", (genres ?? HalloweenGenres).Select(g => $$"""{"id": {{g.Id}}, "name": {{Text(g.Name)}}}"""))}}],
          "id": 948,
          "imdb_id": "tt0077651",
          "original_title": {{Text(title)}},
          "overview": {{Text(overview)}},
          "poster_path": {{Text(posterPath)}},
          "release_date": {{Text(releaseDate)}},
          "runtime": {{(runtime is { } minutes ? minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},
          "status": "Released",
          "title": {{Text(title)}},
          "credits": {
            "cast": [{{string.Join(", ", (cast ?? HalloweenCast).Select(c => $$"""{"adult": false, "character": "", "id": 1, "name": {{Text(c.Name)}}, "order": {{c.Order}}}"""))}}],
            "crew": [{{string.Join(", ", (crew ?? HalloweenCrew).Select(c => $$"""{"department": "", "id": 2, "job": {{Text(c.Job)}}, "name": {{Text(c.Name)}}}"""))}}]
          },
          "release_dates": {
            "results": [{{string.Join(", ", (releases ?? HalloweenReleases).GroupBy(r => r.Country).Select(Country))}}]
          }
        }
        """;

    private static readonly (string? Name, int Order)[] HalloweenCast =
    [
        ("Donald Pleasence", 0),
        ("Jamie Lee Curtis", 1),
        ("Nancy Kyes", 2),
        ("P. J. Soles", 3),
        ("Charles Cyphers", 4),
        ("Kyle Richards", 5),
        ("Brian Andrews", 6),
    ];

    private static readonly (string? Name, string Job)[] HalloweenCrew =
    [
        ("Debra Hill", "Producer"),
        ("John Carpenter", "Director"),
        ("John Carpenter", "Original Music Composer"),
    ];

    // Its US premiere was rated, a festival screening wasn't, and the theatrical release was.
    private static readonly (string Country, string? Certification)[] HalloweenReleases =
    [
        ("GB", "18"),
        ("US", "R"),
        ("US", ""),
        ("US", "R"),
    ];

    private static readonly (int Id, string? Name)[] HalloweenGenres = [(HorrorId, "Horror"), (ThrillerId, "Thriller")];

    private static string Country(IGrouping<string, (string Country, string? Certification)> releases) =>
        $$"""
        {
          "iso_3166_1": {{Text(releases.Key)}},
          "release_dates": [{{string.Join(", ", releases.Select(r => $$"""{"certification": {{Text(r.Certification)}}, "descriptors": [], "iso_639_1": "", "note": "", "release_date": "1978-10-25T00:00:00.000Z", "type": 3}"""))}}]
        }
        """;

    // A JSON string, quoted and escaped, or null.
    private static string Text(string? value) => JsonSerializer.Serialize(value);
}
