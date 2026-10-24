using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class UpcItemDbClientTests : IDisposable
{
    private static readonly UpcAnswer NotFound = new(UpcStatus.NotFound, null, null, null);

    private readonly FakeUpcItemDb _upcItemDb = new();
    private readonly HttpClient _http;

    public UpcItemDbClientTests() => _http = new HttpClient(_upcItemDb);

    public void Dispose() => _http.Dispose();

    // What HttpClient throws when the register is offline, and when UPCitemdb takes longer than LookupTimeout.
    public static TheoryData<Exception> NoAnswer => new()
    {
        new HttpRequestException("No such host is known."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()),
    };

    // No items, or a first item without a title, is UPCitemdb not knowing the product.
    public static TheoryData<string> NoTitle => new()
    {
        UpcJson.NoItems,
        UpcJson.Items(""),
        UpcJson.Items((string?)null),
        UpcJson.Items("\u200C \u200D"), // joiners that join nothing
    };

    // The free plan needs no key, so the request carries the barcode and asks for JSON, and that's all.
    [Fact]
    public async Task LookupAsync_AnyUpc_AsksForJsonWithNoKey()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);

        await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        var request = Assert.Single(_upcItemDb.Requests);
        Assert.Equal(new Uri("https://api.upcitemdb.com/prod/trial/lookup?upc=032429258373"), request.Uri);
        Assert.Equal("Accept", Assert.Single(request.Headers.Keys));
        Assert.Equal("application/json", request.Headers["Accept"]);
    }

    [Fact]
    public async Task LookupAsync_BaseUrlInTheOptions_AsksThere()
    {
        await Client(baseUrl: new Uri("https://upcitemdb.test/v9/")).LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://upcitemdb.test/v9/lookup?upc=032429258373"), Assert.Single(_upcItemDb.Requests).Uri);
    }

    // Rewind's form: a 13-digit EAN that starts with 0 is the same product as its UPC-A.
    [Fact]
    public async Task LookupAsync_EanWithALeadingZero_AsksForItsUpcA()
    {
        await Client().LookupAsync(UpcJson.TommyBoyEan, TestContext.Current.CancellationToken);

        Assert.Equal(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc), Assert.Single(_upcItemDb.Requests).Uri.PathAndQuery);
    }

    // SEC-5: only a factory barcode goes into the URL.
    [Theory]
    [InlineData("032429258374")] // the check digit is wrong
    [InlineData("RV-00042")]
    [InlineData("032429258373&upc=1")]
    public async Task LookupAsync_NotAFactoryBarcode_AsksNothing(string code)
    {
        var answer = await Client().LookupAsync(code, TestContext.Current.CancellationToken);

        Assert.Equal(UpcStatus.NotFound, answer.Status);
        Assert.Empty(_upcItemDb.Requests);
    }

    // The plan's own example, in UPCitemdb's real answer.
    [Fact]
    public async Task LookupAsync_TommyBoy_FindsItsProductTitle()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.Found, "Tommy Boy (DVD)", null, null), answer);
    }

    [Fact]
    public async Task LookupAsync_TitleWithSpacesAround_IsTrimmed()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items("  Tommy Boy (DVD) "));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal("Tommy Boy (DVD)", answer.ProductTitle);
    }

    // SEC-5: answers are capped, however long UPCitemdb's title is.
    [Fact]
    public async Task LookupAsync_LongTitle_IsCut()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items(new string('x', 5000)));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcItemDbClient.MaxTitleLength, answer.ProductTitle?.Length);
    }

    // A cut leaves no space at the end, nor a joiner, which joins nothing there.
    [Fact]
    public async Task LookupAsync_LongTitleCutAfterASpace_IsTrimmed()
    {
        var kept = new string('x', UpcItemDbClient.MaxTitleLength - 2);
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items(kept + " \u200Dyyy"));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(kept, answer.ProductTitle);
    }

    // SEC-5: a line break or another control character in a title never reaches the screen, the
    // database or TMDB's query, and the words either side of it stay apart. Unicode's line and
    // paragraph separators are line breaks too.
    [Theory]
    [InlineData("Tommy\nBoy\t(DVD)\u0000")]
    [InlineData("Tommy\u2028Boy\u2029(DVD)")]
    public async Task LookupAsync_TitleWithControlCharactersOrSeparators_HasSpacesInstead(string title)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items(title));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal("Tommy Boy (DVD)", answer.ProductTitle);
    }

    // SEC-5: invisible characters that change how text looks, such as a right-to-left override, a
    // zero-width space or a tag character, go.
    [Fact]
    public async Task LookupAsync_TitleWithFormatCharacters_DropsThem()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items("‮Tommy​ Boy⁦ (DVD)󠁁"));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal("Tommy Boy (DVD)", answer.ProductTitle);
    }

    // Except the zero-width non-joiner and joiner, which writing such as Persian, as in Children of
    // Heaven's own title, and emoji such as a family need to show as they're meant to.
    [Theory]
    [InlineData("بچه\u200Cهای آسمان")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 Movie Night (DVD)")]
    public async Task LookupAsync_TitleWithJoiners_KeepsThem(string title)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items(title));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(title, answer.ProductTitle);
    }

    // Only the first item counts, as wall sheet R4 says, so a blank first title isn't covered for
    // by a second.
    [Theory]
    [InlineData(new[] { "Tommy Boy (DVD)", "Tommy Boy (Blu-ray)" }, "Tommy Boy (DVD)")]
    [InlineData(new[] { " ", "Tommy Boy (DVD)" }, null)]
    public async Task LookupAsync_SeveralItems_TakesTheFirstTitle(string[] titles, string? expected)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items(titles));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(expected, answer.ProductTitle);
    }

    [Theory]
    [MemberData(nameof(NoTitle))]
    public async Task LookupAsync_NoTitleInTheAnswer_IsNotFound(string json)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, json);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(NotFound, answer);
    }

    [Fact]
    public async Task LookupAsync_UpcItemDbsNotFound_IsNotFound()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Error("NOT_FOUND", "No match found"), HttpStatusCode.NotFound);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(NotFound, answer);
    }

    // A 404 is saved as not found for a month, and UPCitemdb's docs say its 404 also means a wrong
    // address, so only its own NOT_FOUND counts. A proxy's, a Wi-Fi sign-in page's or a CDN's 404,
    // or a 404 with another code, is trouble, and never saved. A page in a character set .NET
    // can't decode isn't even read (SEC-5).
    [Fact]
    public async Task LookupAsync_404ThatIsNotUpcItemDbs_IsTryLater()
    {
        _upcItemDb.AnswerAs(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc), "<html>Not here</html>", "text/html; charset=windows-1252", HttpStatusCode.NotFound);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.ServerError, null), answer);
    }

    [Fact]
    public async Task LookupAsync_404WithAnotherCode_IsTryLater()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Error("X", "Not here"), HttpStatusCode.NotFound);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.ServerError, answer.Reason);
    }

    // The same bar for a 200: only UPCitemdb's own OK, with a list of items, is a lookup's answer.
    [Theory]
    [InlineData("""{"code": "X", "items": []}""")]
    [InlineData("""{"items": []}""")]
    [InlineData("""{"code": "OK"}""")]
    public async Task LookupAsync_200ThatIsNotALookupsAnswer_IsTryLater(string json)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, json);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.ServerError, null), answer);
    }

    // NET-12: UPCitemdb's refusals are outcomes, with the reason, never exceptions.
    [Fact]
    public async Task LookupAsync_TooFast_IsTryLaterForThat()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TooFast, HttpStatusCode.TooManyRequests, remaining: 0);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.TooFast, null), answer);
    }

    // UPCitemdb's docs: once the day's lookups are used up, Retry-After says in how many seconds
    // they come back.
    [Fact]
    public async Task LookupAsync_ExceedLimit_SaysWhenTheLookupsComeBack()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.ExceedLimit, HttpStatusCode.TooManyRequests, remaining: 0, retryAfter: "3600");

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.DailyLimit, TimeSpan.FromHours(1)), answer);
    }

    // SEC-5: a Retry-After that isn't a number of seconds, or is none, leaves the pacer to choose.
    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("0")]
    public async Task LookupAsync_ExceedLimitWithoutAUsableRetryAfter_SaysNothingOfWhen(string? retryAfter)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.ExceedLimit, HttpStatusCode.TooManyRequests, remaining: 0, retryAfter: retryAfter);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.DailyLimit, null), answer);
    }

    // "Too fast" ends with the burst window, so Retry-After is read only for the day's limit.
    [Fact]
    public async Task LookupAsync_TooFastWithRetryAfter_SaysNothingOfWhen()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TooFast, HttpStatusCode.TooManyRequests, remaining: 0, retryAfter: "3600");

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Null(answer.RetryAfter);
    }

    // Any 429 means wait, so a 429 that doesn't say which limit, such as one from the gateway in
    // front of UPCitemdb, or whose answer is cut off, still starts the cooldown, as too fast.
    [Fact]
    public async Task LookupAsync_429ThatIsNotJson_IsTryLaterForTooFast()
    {
        _upcItemDb.AnswerAs(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc), "<html>Slow down</html>", "text/html; charset=windows-1252", HttpStatusCode.TooManyRequests);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.TooFast, answer.Reason);
    }

    [Fact]
    public async Task LookupAsync_429CutOff_IsTryLaterForTooFast()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, """{"code": "EXCEED_LI""", HttpStatusCode.TooManyRequests, remaining: 0);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.TooFast, answer.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // INVALID_UPC or INVALID_QUERY
    [InlineData(HttpStatusCode.InternalServerError)] // SERVER_ERR
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task LookupAsync_UpcItemDbHasTrouble_IsTryLaterForAServerError(HttpStatusCode status)
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Error("SERVER_ERR", "Server error"), status);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.ServerError, null), answer);
    }

    // SEC-5: such as a Wi-Fi sign-in page, which answers every address with 200 OK, so the
    // register isn't really online.
    [Fact]
    public async Task LookupAsync_AnswerIsNotJson_IsTryLaterForOffline()
    {
        _upcItemDb.AnswerHtml(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc));

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.Offline, answer.Reason);
    }

    // SEC-5: JSON that isn't in UPCitemdb's shape, such as an answer cut off partway, is trouble,
    // not an exception.
    [Fact]
    public async Task LookupAsync_AnswerCutOff_IsTryLaterForAServerError()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, """{"code": "OK", "total": 1, "items": [{"title": "Tommy""");

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.ServerError, answer.Reason);
    }

    // SEC-5: an answer over the cap is refused, even when it's valid JSON.
    [Fact]
    public async Task LookupAsync_AnswerTooBig_IsTryLaterForAServerError()
    {
        var huge = $$"""{"code": "OK", "items": [], "padding": "{{new string('x', UpcItemDbClient.MaxAnswerBytes)}}"}""";
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, huge);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(UpcTryLaterReason.ServerError, answer.Reason);
    }

    [Theory]
    [MemberData(nameof(NoAnswer))]
    public async Task LookupAsync_NoAnswer_IsTryLaterForOffline(Exception failure)
    {
        _upcItemDb.Throw(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc), failure);

        var answer = await Client().LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        Assert.Equal(new UpcAnswer(UpcStatus.TryLater, null, UpcTryLaterReason.Offline, null), answer);
    }

    // However an UpcItemDbClient is made, it waits no longer than this for UPCitemdb.
    [Fact]
    public void Constructor_NewHttpClient_WaitsTenSecondsAtMost()
    {
        _ = Client();

        Assert.Equal(TimeSpan.FromSeconds(10), _http.Timeout);
    }

    // A screen that closes, or a clerk who presses Esc, cancels a lookup, and that's not UPCitemdb
    // failing to answer.
    [Fact]
    public async Task LookupAsync_CallerCancels_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().LookupAsync(UpcJson.TommyBoyUpc, cancelled.Token));
    }

    // OBS-3: each answer at Debug with the lookups UPCitemdb says are left, but nothing from the
    // answer's body.
    [Fact]
    public async Task LookupAsync_Found_LogsTheOutcomeAndLookupsLeftButNotTheTitle()
    {
        var log = new ListLogger<UpcItemDbClient>();
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy, remaining: 42);

        await Client(logger: log).LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("032429258373 on UPCitemdb: Found, 42 lookups left", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Tommy", entry.Message, StringComparison.Ordinal);
    }

    // OBS-3: a lookup the clerk has to make up for is a handled problem.
    [Fact]
    public async Task LookupAsync_TooFast_LogsAWarning()
    {
        var log = new ListLogger<UpcItemDbClient>();
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TooFast, HttpStatusCode.TooManyRequests, remaining: 0);

        await Client(logger: log).LookupAsync(UpcJson.TommyBoyUpc, TestContext.Current.CancellationToken);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("429 application/json: TooFast, 0 lookups left", entry.Message, StringComparison.Ordinal);
    }

    private UpcItemDbClient Client(Uri? baseUrl = null, ILogger<UpcItemDbClient>? logger = null)
    {
        var options = new UpcItemDbOptions();
        options.BaseUrl = baseUrl ?? options.BaseUrl;
        return new UpcItemDbClient(_http, Options.Create(options), logger ?? NullLogger<UpcItemDbClient>.Instance);
    }
}
