using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Metadata;

/// <summary>
/// Searches TMDB for movies and fetches the one the clerk picks, with its poster (Build Plan 2.3).
/// The API Read Access Token goes as a Bearer header to TMDB's API only, never to its image server
/// (SEC-1). Every answer is untrusted (SEC-5): only JSON is read, text is trimmed and capped, a
/// poster path must look like one, and anything unknown comes back null, never 0, blank or "NR"
/// (DATA-3). When TMDB can't answer, such as when the register is offline, the status says so
/// instead of an exception (NET-12).
/// </summary>
public sealed partial class TmdbClient
{
    /// <summary>How long Rewind waits for any one answer from TMDB before asking the clerk to try later.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The biggest answer Rewind reads. A movie's details with its whole cast and crew come to tens
    /// of kilobytes, and a poster to about 30, so anything this big is wrong (SEC-5).
    /// </summary>
    public const int MaxAnswerBytes = WebAnswers.MaxBytes;

    /// <summary>How many of a movie's top-billed actors Rewind keeps (wall sheet R4).</summary>
    public const int CastSize = 5;

    /// <summary>The longest title or name Rewind keeps (SEC-5). TMDB's are far shorter.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest overview Rewind keeps (SEC-5). TMDB's are far shorter.</summary>
    public const int MaxOverviewLength = 2000;

    // The longest poster path PosterPathPattern takes: a slash, 64 letters and digits, then .jpg.
    private const int MaxPosterPathLength = 69;

    private const string Bearer = "Bearer";
    private const string ReleaseDateFormat = "yyyy-MM-dd";
    private const string DirectorJob = "Director";
    private const string UnitedStates = "US";

    private static readonly Uri Api = new("https://api.themoviedb.org/3/");
    private static readonly Uri Images = new("https://image.tmdb.org/t/p/");

    // The US ratings Rewind keeps. TMDB's list also has NR, "No rating information", which is
    // unknown and so NULL (DATA-3), as is anything else, such as a TV rating.
    private static readonly string[] UsRatings = ["G", "PG", "PG-13", "R", "NC-17"];

    // TMDB writes its names in snake_case, such as release_date. The one name the policy can't
    // make, iso_3166_1, has its own attribute below.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly TmdbOptions _options;
    private readonly ILogger<TmdbClient> _logger;

    /// <summary>
    /// Gets a client ready. It sets the HttpClient's timeout and its cap on answers itself, so
    /// every TmdbClient has them however it's made (SEC-5). The HttpClient must therefore be one
    /// that hasn't sent anything yet, as AddHttpClient gives each TmdbClient. A plain constructor,
    /// since a primary one can't do this work (NET-10).
    /// </summary>
    public TmdbClient(HttpClient http, IOptions<TmdbOptions> options, ILogger<TmdbClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        http.Timeout = RequestTimeout;
        http.MaxResponseContentBufferSize = MaxAnswerBytes;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Searches TMDB's movies by title, leaving out adult ones, and returns the first page of
    /// matches, best first. A blank title finds nothing without asking TMDB.
    /// </summary>
    public async Task<TmdbSearch> SearchAsync(string title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (WebAnswers.Clean(title, MaxNameLength) is not { } query)
        {
            return new TmdbSearch(TmdbStatus.Ok, []);
        }

        // Escaped, so a title such as "Tom & Jerry" stays one query (SEC-5).
        var (status, reply) = await GetAsync<SearchReply>(
            $"search/movie?query={Uri.EscapeDataString(query)}&include_adult=false", cancellationToken);
        if (reply is null)
        {
            return new TmdbSearch(status, []);
        }

        var matches = (reply.Results ?? []).Select(ToMatch).OfType<TmdbMatch>().ToList();
        LogSearched(_logger, query, matches.Count);
        return new TmdbSearch(TmdbStatus.Ok, matches);
    }

    /// <summary>
    /// Fetches a movie's details, with its cast, crew and release dates in the same call (wall
    /// sheet R4), and keeps what Rewind stores: the US rating, the director, the top-billed cast,
    /// the genres and where TMDB keeps the poster.
    /// </summary>
    public async Task<TmdbDetails> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmdbId);

        var (status, reply) = await GetAsync<MovieReply>(
            string.Create(CultureInfo.InvariantCulture, $"movie/{tmdbId}?append_to_response=credits,release_dates"),
            cancellationToken);
        if (reply is null)
        {
            return new TmdbDetails(status, null);
        }

        // Movies.Title can't be blank, so a movie without one can't be added.
        if (WebAnswers.Clean(reply.Title, MaxNameLength) is not { } title)
        {
            LogUntitled(_logger, tmdbId);
            return new TmdbDetails(TmdbStatus.NotFound, null);
        }

        // Under the ID of the match the clerk picked. The answer's own id isn't read.
        return new TmdbDetails(TmdbStatus.Ok, new TmdbMovie(
            tmdbId,
            title,
            YearOf(reply.ReleaseDate),
            reply.Runtime > 0 ? reply.Runtime : null, // TMDB sends 0 when it doesn't know (DATA-3)
            UsRating(reply.ReleaseDates),
            Directors(reply.Credits),
            WebAnswers.Clean(reply.Overview, MaxOverviewLength),
            TrustedPosterPath(reply.PosterPath),
            TopCast(reply.Credits),
            Genres(reply.Genres)));
    }

    /// <summary>
    /// Downloads a poster at one of Rewind's sizes. Returns null when it can't: the path isn't one
    /// Rewind trusts, TMDB has no such poster or doesn't answer, or what came back isn't a JPEG.
    /// The poster comes from TMDB's image server, so no token goes with it.
    /// </summary>
    /// <param name="posterPath">Where TMDB keeps the poster, from a <see cref="TmdbMatch"/> or a <see cref="TmdbMovie"/>.</param>
    /// <param name="size">Which size to download.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    public async Task<byte[]?> GetPosterAsync(string posterPath, PosterSize size, CancellationToken cancellationToken)
    {
        if (TrustedPosterPath(posterPath) is null)
        {
            return null;
        }

        try
        {
            var poster = await _http.GetByteArrayAsync(new Uri(Images, WidthOf(size) + posterPath), cancellationToken);
            if (poster is [0xFF, 0xD8, 0xFF, ..]) // every JPEG starts like this
            {
                return poster;
            }

            LogNotJpeg(_logger, posterPath);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is HttpRequestException or TaskCanceledException)
        {
            LogPosterFailed(_logger, e, posterPath);
        }

        return null;
    }

    // Asks TMDB's API with the token and reads the answer. A reply comes back only with Ok.
    private async Task<(TmdbStatus Status, T? Reply)> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
        where T : class
    {
        var token = _options.AccessToken;
        if (token.Length == 0)
        {
            LogNoToken(_logger);
            return (TmdbStatus.NotSetUp, null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Api, relativeUri));
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, token);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK when !WebAnswers.IsJsonInUtf8(response.Content.Headers.ContentType):
                    LogNotJson(_logger, response.Content.Headers.ContentType?.MediaType);
                    return (TmdbStatus.TryLater, null);
                case HttpStatusCode.OK:
                    var reply = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                    return reply is null ? (TmdbStatus.TryLater, null) : (TmdbStatus.Ok, reply);
                case HttpStatusCode.Unauthorized:
                    LogTokenRefused(_logger);
                    return (TmdbStatus.NotSetUp, null);
                case HttpStatusCode.NotFound:
                    return (TmdbStatus.NotFound, null);
                default: // such as 429, too many requests, or a 5xx while TMDB has trouble
                    LogRefused(_logger, (int)response.StatusCode);
                    return (TmdbStatus.TryLater, null);
            }
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline, no answer within RequestTimeout, an answer over MaxAnswerBytes, or JSON that
            // isn't TMDB's. The clerk is asked to try again later.
            LogNoAnswer(_logger, e);
            return (TmdbStatus.TryLater, null);
        }
    }

    private TmdbMatch? ToMatch(SearchHit hit) =>
        hit.Id > 0 && WebAnswers.Clean(hit.Title, MaxNameLength) is { } title
            ? new TmdbMatch(hit.Id, title, YearOf(hit.ReleaseDate), WebAnswers.Clean(hit.Overview, MaxOverviewLength), TrustedPosterPath(hit.PosterPath))
            : null;

    // TMDB sends an unknown release date as "" (DATA-3).
    private static int? YearOf(string? releaseDate) =>
        DateOnly.TryParseExact(releaseDate, ReleaseDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Year
            : null;

    // The first of the US releases, in TMDB's order, with a rating Rewind knows. Festival
    // screenings and re-releases often have none, and NR means none (wall sheet R4).
    private static string? UsRating(ReleaseDatesReply? releaseDates) =>
        (releaseDates?.Results ?? [])
            .Where(country => country.Iso31661 == UnitedStates)
            .SelectMany(country => country.ReleaseDates ?? [])
            .Select(release => Array.Find(UsRatings, rating => string.Equals(rating, release.Certification?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(rating => rating is not null);

    // Some movies have two, such as The Matrix.
    private static string? Directors(CreditsReply? credits)
    {
        var names = (credits?.Crew ?? [])
            .Where(person => person.Job == DirectorJob)
            .Select(person => WebAnswers.Clean(person.Name, MaxNameLength))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? null : WebAnswers.Clean(string.Join(", ", names), MaxNameLength);
    }

    // TMDB numbers the billing from 0. A blank name is left out, and the next actor takes its place.
    private static List<string> TopCast(CreditsReply? credits) =>
        (credits?.Cast ?? [])
            .OrderBy(actor => actor.Order)
            .Select(actor => WebAnswers.Clean(actor.Name, MaxNameLength))
            .OfType<string>()
            .Take(CastSize)
            .ToList();

    // A genre listed twice would link the movie to it twice, which MovieGenres' key refuses.
    private static List<TmdbGenre> Genres(List<GenreReply>? genres) =>
        (genres ?? [])
            .Where(genre => genre.Id > 0)
            .Select(genre => WebAnswers.Clean(genre.Name, MaxNameLength) is { } name ? new TmdbGenre(genre.Id, name) : null)
            .OfType<TmdbGenre>()
            .DistinctBy(genre => genre.TmdbId)
            .ToList();

    // A poster path becomes part of a URL, so only TMDB's own form gets through: a slash, letters
    // and digits, then .jpg, such as /wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg (SEC-5). The length is checked
    // before the pattern runs, as wall sheet R4 says. A path that's turned away is logged, so a
    // change in TMDB's paths, such as .png posters, shows in the log rather than only as posters
    // that stop appearing; one that's too long is logged by its length alone.
    private string? TrustedPosterPath(string? posterPath)
    {
        if (posterPath is null)
        {
            return null;
        }

        if (posterPath.Length > MaxPosterPathLength)
        {
            LogLongPosterPath(_logger, posterPath.Length);
            return null;
        }

        if (!PosterPathPattern().IsMatch(posterPath))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var printable = Printable(posterPath);
                LogUntrustedPosterPath(_logger, printable);
            }

            return null;
        }

        return posterPath;
    }

    // SEC-5: text from TMDB could hold a line break, which would forge a line in the log, so
    // anything outside printable ASCII is written as a question mark, as an unreadable scan is.
    private static string Printable(string text) =>
        new(text.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());

    // TMDB's names for its widths: w92, w342 and w154 (wall sheet R4).
    private static string WidthOf(PosterSize size) => size switch
    {
        PosterSize.Picker => "w92",
        PosterSize.Register => "w342",
        PosterSize.Thumb => "w154",
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Rewind has no such poster size."),
    };

    // [0-9A-Za-z], never \w: in .NET, \w matches letters and digits in every script (SEC-5).
    [GeneratedRegex("^/[0-9A-Za-z]{1,64}\\.jpg$")]
    private static partial Regex PosterPathPattern();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searched TMDB for {Title}: {Count} matches")]
    private static partial void LogSearched(ILogger logger, string title, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB movie {TmdbId} has no title, so it can't be added")]
    private static partial void LogUntitled(ILogger logger, int tmdbId);

    [LoggerMessage(Level = LogLevel.Error, Message = "TMDB isn't set up: Tmdb:AccessToken is empty. The README says where to put the token")]
    private static partial void LogNoToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "TMDB refused Rewind's token. Check that Tmdb:AccessToken is TMDB's API Read Access Token, the long one, not the API Key")]
    private static partial void LogTokenRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB answered {StatusCode}; the clerk is asked to try again later")]
    private static partial void LogRefused(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB's answer was {MediaType}, not JSON in UTF-8, so it's left unread; the clerk is asked to try again later")]
    private static partial void LogNotJson(ILogger logger, string? mediaType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB didn't answer, or its answer was too big or wasn't TMDB's JSON; the clerk is asked to try again later")]
    private static partial void LogNoAnswer(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Couldn't download the poster {PosterPath}")]
    private static partial void LogPosterFailed(ILogger logger, Exception exception, string posterPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The poster {PosterPath} isn't a JPEG, so it's left out")]
    private static partial void LogNotJpeg(ILogger logger, string posterPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Left out a poster path that isn't in TMDB's usual form: {PosterPath}")]
    private static partial void LogUntrustedPosterPath(ILogger logger, string posterPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Left out a poster path of {Length} characters, longer than TMDB's")]
    private static partial void LogLongPosterPath(ILogger logger, int length);

    // TMDB's JSON, keeping only what Rewind reads.
    private sealed record SearchReply(List<SearchHit>? Results);

    private sealed record SearchHit(int Id, string? Title, string? ReleaseDate, string? Overview, string? PosterPath);

    private sealed record MovieReply(
        string? Title,
        string? ReleaseDate,
        int? Runtime,
        string? Overview,
        string? PosterPath,
        List<GenreReply>? Genres,
        CreditsReply? Credits,
        ReleaseDatesReply? ReleaseDates);

    private sealed record GenreReply(int Id, string? Name);

    private sealed record CreditsReply(List<CastReply>? Cast, List<CrewReply>? Crew);

    private sealed record CastReply(string? Name, int Order);

    private sealed record CrewReply(string? Name, string? Job);

    private sealed record ReleaseDatesReply(List<CountryReply>? Results);

    // The policy would make this iso31661, and every rating would come back NULL (wall sheet R4).
    private sealed record CountryReply([property: JsonPropertyName("iso_3166_1")] string? Iso31661, List<ReleaseReply>? ReleaseDates);

    private sealed record ReleaseReply(string? Certification);
}
