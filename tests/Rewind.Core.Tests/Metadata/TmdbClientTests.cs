using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class TmdbClientTests : IDisposable
{
    private const string Token = "eyJ.test-token.signature";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly FakeTmdb _tmdb = new();
    private readonly HttpClient _http;

    public TmdbClientTests() => _http = new HttpClient(_tmdb);

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task SearchAsync_AnyTitle_SendsTheTokenAsABearerHeader()
    {
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);

        await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        var request = Assert.Single(_tmdb.Requests);
        Assert.Equal(new Uri("https://api.themoviedb.org/3/search/movie?query=Halloween&include_adult=false"), request.Uri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
    }

    // SEC-1: only TMDB's API needs the token, so it never goes to the image server, even after
    // the same client has sent it to the API.
    [Fact]
    public async Task GetPosterAsync_AfterASearch_SendsNoToken()
    {
        var client = Client();
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), Jpeg);
        await client.SearchAsync("Halloween", TestContext.Current.CancellationToken);

        await client.GetPosterAsync(TmdbJson.HalloweenPoster, PosterSize.Register, TestContext.Current.CancellationToken);

        var request = _tmdb.Requests[^1];
        Assert.Equal("image.tmdb.org", request.Uri.Host);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task SearchAsync_Halloween_FindsEachMatchWithItsYear()
    {
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.Ok, search.Status);
        Assert.Equal(
            [(948, "Halloween", (int?)1978), (424139, "Halloween", 2018), (1446523, "Halloween Store", null)],
            search.Matches.Select(m => (m.TmdbId, m.Title, m.Year)));
        Assert.Equal(TmdbJson.HalloweenPoster, search.Matches[0].PosterPath);
    }

    // DATA-3: TMDB sends an unknown release date as "", and an unknown overview as "".
    [Fact]
    public async Task SearchAsync_EmptyReleaseDateAndOverview_AreUnknown()
    {
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        var store = search.Matches.Single(m => m.TmdbId == TmdbJson.HalloweenStoreId);
        Assert.Null(store.Year);
        Assert.Null(store.Overview);
        Assert.Null(store.PosterPath);
    }

    // SEC-5: an ampersand in a title must not start a second parameter in the query.
    [Fact]
    public async Task SearchAsync_TitleWithAmpersand_StaysOneQuery()
    {
        await Client().SearchAsync("Tom & Jerry", TestContext.Current.CancellationToken);

        var request = Assert.Single(_tmdb.Requests);
        Assert.Equal(FakeTmdb.SearchFor("Tom%20%26%20Jerry"), request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task SearchAsync_BlankTitle_AsksTmdbNothing()
    {
        var search = await Client().SearchAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.Ok, search.Status);
        Assert.Empty(search.Matches);
        Assert.Empty(_tmdb.Requests);
    }

    // GEN-7: without a token, intake says TMDB isn't set up, and nothing is sent.
    [Fact]
    public async Task SearchAsync_NoToken_IsNotSetUp()
    {
        var search = await Client(token: "").SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.NotSetUp, search.Status);
        Assert.Empty(_tmdb.Requests);
    }

    // NET-12: TMDB's refusals are outcomes, never exceptions.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TmdbStatus.NotSetUp)]
    [InlineData(HttpStatusCode.TooManyRequests, TmdbStatus.TryLater)]
    [InlineData(HttpStatusCode.InternalServerError, TmdbStatus.TryLater)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TmdbStatus.TryLater)]
    public async Task SearchAsync_TmdbRefuses_SaysWhy(HttpStatusCode refusal, TmdbStatus expected)
    {
        _tmdb.AnswerStatus(FakeTmdb.SearchFor("Halloween"), refusal);

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(expected, search.Status);
        Assert.Empty(search.Matches);
    }

    // What HttpClient throws when the register is offline, and when TMDB takes longer than Timeout.
    public static TheoryData<Exception> NoAnswer => new()
    {
        new HttpRequestException("No such host is known."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()),
    };

    [Theory]
    [MemberData(nameof(NoAnswer))]
    public async Task SearchAsync_NoAnswer_IsTryLater(Exception failure)
    {
        _tmdb.Throw(FakeTmdb.SearchFor("Halloween"), failure);

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.TryLater, search.Status);
    }

    // SEC-5: such as a Wi-Fi sign-in page, which answers every address with 200 OK.
    [Fact]
    public async Task SearchAsync_AnswerIsNotJson_IsTryLater()
    {
        _tmdb.AnswerHtml(FakeTmdb.SearchFor("Halloween"));

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.TryLater, search.Status);
    }

    // SEC-5: JSON that isn't in TMDB's shape, such as an answer cut off partway, is TMDB not
    // answering, not an exception.
    [Fact]
    public async Task SearchAsync_AnswerCutOff_IsTryLater()
    {
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), """{"page": 1, "results": [{"id": 948, "title": "Hallo""");

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.TryLater, search.Status);
    }

    // SEC-5: TMDB's own Content-Type is application/json in UTF-8. Anything else is left unread,
    // since .NET can't decode some character sets and would throw instead of failing.
    [Fact]
    public async Task SearchAsync_AnswerInAnotherCharacterSet_IsTryLater()
    {
        _tmdb.AnswerAs(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch, "application/json; charset=windows-1252");

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.TryLater, search.Status);
    }

    // Media types and character sets are case-insensitive, so this is TMDB's JSON all the same.
    [Fact]
    public async Task SearchAsync_MediaTypeInCapitals_IsRead()
    {
        _tmdb.AnswerAs(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch, "Application/JSON; charset=UTF-8");

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.Ok, search.Status);
        Assert.Equal(3, search.Matches.Count);
    }

    // SEC-5: an answer over the cap is refused, even when it's valid JSON.
    [Fact]
    public async Task SearchAsync_AnswerTooBig_IsTryLater()
    {
        var huge = $$"""{"page": 1, "results": [], "padding": "{{new string('x', TmdbClient.MaxAnswerBytes)}}"}""";
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), huge);

        var search = await Client().SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.TryLater, search.Status);
    }

    // However a TmdbClient is made, it waits no longer than this for TMDB.
    [Fact]
    public void Constructor_NewHttpClient_WaitsTenSecondsAtMost()
    {
        _ = Client();

        Assert.Equal(TimeSpan.FromSeconds(10), _http.Timeout);
    }

    // A screen that closes cancels its search, and that's not TMDB failing to answer.
    [Fact]
    public async Task SearchAsync_CallerCancels_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().SearchAsync("Halloween", cancelled.Token));
    }

    // SEC-7: even when TMDB refuses it, the token never reaches the log.
    [Fact]
    public async Task SearchAsync_TokenRefused_LogsNoToken()
    {
        var log = new ListLogger<TmdbClient>();
        _tmdb.AnswerStatus(FakeTmdb.SearchFor("Halloween"), HttpStatusCode.Unauthorized);

        await Client(logger: log).SearchAsync("Halloween", TestContext.Current.CancellationToken);

        Assert.NotEmpty(log.Entries);
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains(Token, StringComparison.Ordinal));
    }

    // The Build Plan 2.3 check, as far as TMDB's side goes.
    [Fact]
    public async Task GetMovieAsync_Halloween_TakesRatingDirectorAndTopFiveCast()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie());

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.Ok, details.Status);
        var movie = Assert.IsType<TmdbMovie>(details.Movie);
        Assert.Equal("R", movie.Rating);
        Assert.Equal("John Carpenter", movie.Director);
        Assert.Equal(["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"], movie.Cast);
    }

    [Fact]
    public async Task GetMovieAsync_Halloween_TakesYearRuntimeGenresAndPoster()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie());

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        var movie = Assert.IsType<TmdbMovie>(details.Movie);
        Assert.Equal((TmdbJson.HalloweenId, "Halloween", 1978, 91), (movie.TmdbId, movie.Title, movie.Year, movie.RuntimeMinutes));
        Assert.Equal([new TmdbGenre(27, "Horror"), new TmdbGenre(53, "Thriller")], movie.Genres);
        Assert.Equal(TmdbJson.HalloweenPoster, movie.PosterPath);
    }

    // DATA-3: TMDB sends an unknown runtime as 0, such as for Halloween Store.
    [Fact]
    public async Task GetMovieAsync_RuntimeZero_IsUnknown()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenStoreId), TmdbJson.Movie(title: "Halloween Store", runtime: 0));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenStoreId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.RuntimeMinutes);
    }

    // DATA-3 and wall sheet R4: the first US release with a rating Rewind knows. "" and TMDB's NR
    // ("No rating information") are unknown, and so is anything else, such as a TV rating.
    [Theory]
    [InlineData(new[] { "R" }, "R")]
    [InlineData(new[] { "" }, null)]
    [InlineData(new[] { "NR" }, null)]
    [InlineData(new[] { "", "NR", "PG-13", "R" }, "PG-13")]
    [InlineData(new[] { "TV-14" }, null)]
    [InlineData(new[] { " pg " }, "PG")]
    public async Task GetMovieAsync_UsCertifications_TakesTheFirstRating(string[] certifications, string? expected)
    {
        var releases = certifications.Select(c => ("US", (string?)c)).ToArray();
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(releases: releases));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(expected, details.Movie?.Rating);
    }

    [Fact]
    public async Task GetMovieAsync_OnlyOtherCountriesRated_HasNoRating()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(releases: [("GB", "18"), ("DE", "16")]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.Rating);
    }

    // Such as The Matrix.
    [Fact]
    public async Task GetMovieAsync_TwoDirectors_NamesBoth()
    {
        _tmdb.AnswerJson(
            FakeTmdb.MovieFor(TmdbJson.HalloweenId),
            TmdbJson.Movie(crew: [("Lana Wachowski", "Director"), ("Joel Silver", "Producer"), ("Lilly Wachowski", "Director")]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal("Lana Wachowski, Lilly Wachowski", details.Movie?.Director);
    }

    [Fact]
    public async Task GetMovieAsync_NoDirector_IsUnknown()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(crew: [("Debra Hill", "Producer")]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.Director);
    }

    [Fact]
    public async Task GetMovieAsync_CastOutOfOrder_KeepsTheTopFiveByBilling()
    {
        _tmdb.AnswerJson(
            FakeTmdb.MovieFor(TmdbJson.HalloweenId),
            TmdbJson.Movie(cast: [("F", 5), ("B", 1), ("D", 3), ("A", 0), ("E", 4), ("C", 2)]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B", "C", "D", "E"], details.Movie?.Cast);
    }

    // MovieCast's names can't be blank, so the next actor takes the place.
    [Fact]
    public async Task GetMovieAsync_BlankActorName_IsLeftOut()
    {
        _tmdb.AnswerJson(
            FakeTmdb.MovieFor(TmdbJson.HalloweenId),
            TmdbJson.Movie(cast: [("A", 0), (" ", 1), ("C", 2), (null, 3), ("E", 4), ("F", 5), ("G", 6)]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(["A", "C", "E", "F", "G"], details.Movie?.Cast);
    }

    [Fact]
    public async Task GetMovieAsync_BlankOverview_IsUnknown()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(overview: " "));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.Overview);
    }

    // SEC-5: answers are capped, however long TMDB's text is.
    [Fact]
    public async Task GetMovieAsync_LongOverview_IsCut()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(overview: new string('x', 5000)));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(TmdbClient.MaxOverviewLength, details.Movie?.Overview?.Length);
    }

    [Fact]
    public async Task GetMovieAsync_GenreListedTwice_KeepsItOnce()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(genres: [(27, "Horror"), (27, "Horror")]));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal([new TmdbGenre(27, "Horror")], details.Movie?.Genres);
    }

    // SEC-5: a poster path goes into a URL, so only TMDB's own form is trusted.
    [Theory]
    [InlineData("/../../keys/secrets.jpg")]
    [InlineData("/wijl ZHaYMvlDTPqJoTCWKFkCPU.jpg")]
    [InlineData("wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg")]
    [InlineData("/wijlZ3HaYMvlDTPqJoTCWKFkCPU.png")]
    [InlineData("/wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg?size=huge")]
    [InlineData("//evil.example/poster.jpg")]
    public async Task GetMovieAsync_UntrustedPosterPath_HasNoPoster(string posterPath)
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(posterPath: posterPath));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.PosterPath);
    }

    // A change in TMDB's poster paths shows in the log, not only as posters that stop appearing.
    [Fact]
    public async Task GetMovieAsync_UntrustedPosterPath_LogsIt()
    {
        var log = new ListLogger<TmdbClient>();
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(posterPath: "/wijlZ3HaYMvlDTPqJoTCWKFkCPU.png"));

        await Client(logger: log).GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Contains(
            log.Entries,
            entry => entry.Level == LogLevel.Debug && entry.Message.Contains("/wijlZ3HaYMvlDTPqJoTCWKFkCPU.png", StringComparison.Ordinal));
    }

    // The longest path the pattern takes, a slash, 64 letters and digits, then .jpg, is still
    // trusted, so the length check and the pattern can't drift apart.
    [Fact]
    public async Task GetMovieAsync_PosterPathAtTheLongestLength_IsTrusted()
    {
        var longest = "/" + new string('a', 64) + ".jpg";
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(posterPath: longest));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(69, longest.Length);
        Assert.Equal(longest, details.Movie?.PosterPath);
    }

    // SEC-5: a line break in a path TMDB sends can't forge a line in the log.
    [Fact]
    public async Task GetMovieAsync_UntrustedPosterPathWithLineBreak_LogsItOnOneLine()
    {
        var log = new ListLogger<TmdbClient>();
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(posterPath: "/abc\n[ERR] forged.jpg"));

        await Client(logger: log).GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        var entry = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("poster path", StringComparison.Ordinal));
        Assert.Contains("/abc?[ERR] forged.jpg", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', entry.Message);
    }

    // SEC-5 and wall sheet R4: the length is checked before the pattern, and only the length is logged.
    [Fact]
    public async Task GetMovieAsync_PosterPathTooLong_LogsItsLength()
    {
        var log = new ListLogger<TmdbClient>();
        var tooLong = "/" + new string('a', 65) + ".jpg";
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(posterPath: tooLong));

        var details = await Client(logger: log).GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Null(details.Movie?.PosterPath);
        var entry = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("poster path", StringComparison.Ordinal));
        Assert.Contains("70 characters", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(tooLong, entry.Message, StringComparison.Ordinal);
    }

    // Movies.Title can't be blank.
    [Fact]
    public async Task GetMovieAsync_NoTitle_IsNotFound()
    {
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie(title: ""));

        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.NotFound, details.Status);
        Assert.Null(details.Movie);
    }

    [Fact]
    public async Task GetMovieAsync_MovieGoneFromTmdb_IsNotFound()
    {
        var details = await Client().GetMovieAsync(TmdbJson.HalloweenId, TestContext.Current.CancellationToken);

        Assert.Equal(TmdbStatus.NotFound, details.Status);
    }

    // Wall sheet R4: w92 for the picker, w342 for the register and w154 for the phone's thumbnails.
    [Theory]
    [InlineData(PosterSize.Picker, "w92")]
    [InlineData(PosterSize.Register, "w342")]
    [InlineData(PosterSize.Thumb, "w154")]
    public async Task GetPosterAsync_EachSize_DownloadsItsWidth(PosterSize size, string width)
    {
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor(width, TmdbJson.HalloweenPoster), Jpeg);

        var poster = await Client().GetPosterAsync(TmdbJson.HalloweenPoster, size, TestContext.Current.CancellationToken);

        Assert.Equal(Jpeg, poster);
    }

    [Fact]
    public async Task GetPosterAsync_AnswerIsNotAJpeg_ReturnsNull()
    {
        _tmdb.AnswerHtml(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster));

        var poster = await Client().GetPosterAsync(TmdbJson.HalloweenPoster, PosterSize.Register, TestContext.Current.CancellationToken);

        Assert.Null(poster);
    }

    [Fact]
    public async Task GetPosterAsync_PosterGoneFromTmdb_ReturnsNull()
    {
        var poster = await Client().GetPosterAsync(TmdbJson.HalloweenPoster, PosterSize.Register, TestContext.Current.CancellationToken);

        Assert.Null(poster);
    }

    [Fact]
    public async Task GetPosterAsync_UntrustedPath_AsksNothing()
    {
        var poster = await Client().GetPosterAsync("/../../keys/secrets.jpg", PosterSize.Register, TestContext.Current.CancellationToken);

        Assert.Null(poster);
        Assert.Empty(_tmdb.Requests);
    }

    private TmdbClient Client(string token = Token, ILogger<TmdbClient>? logger = null) =>
        new(_http, Options.Create(new TmdbOptions { AccessToken = token }), logger ?? NullLogger<TmdbClient>.Instance);
}
