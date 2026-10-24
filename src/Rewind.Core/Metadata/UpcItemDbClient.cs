using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rewind.Core.Scans;

namespace Rewind.Core.Metadata;

/// <summary>
/// Asks UPCitemdb's free plan what product a factory barcode is on, such as "Tommy Boy (DVD)" for
/// 032429258373 (Build Plan 2.2, wall sheet R4). The free plan needs no key, so no key goes with
/// the request. Every answer is untrusted (SEC-5): only UPCitemdb's own JSON is read, and only its
/// own "found" and "not found" count as answers, so a Wi-Fi sign-in page, a proxy's 404 or a wrong
/// address is never saved as one. The title is cleaned and capped. When UPCitemdb can't answer,
/// such as when it says Rewind is asking too fast or the register is offline, the answer says why
/// instead of an exception (NET-12). <see cref="UpcItemDbPacer"/> keeps the calls inside the free
/// plan's limits, and <see cref="BarcodeTitles"/> saves the answers.
/// </summary>
public sealed partial class UpcItemDbClient
{
    /// <summary>How long Rewind waits for UPCitemdb before asking the clerk to type the title.</summary>
    public static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The biggest answer Rewind reads. A lookup's answer, with the shops UPCitemdb lists for the
    /// product, comes to about 5 kilobytes, so anything this big is wrong (SEC-5).
    /// </summary>
    public const int MaxAnswerBytes = WebAnswers.MaxBytes;

    /// <summary>The longest product title Rewind keeps (SEC-5), as long as the longest movie title.</summary>
    public const int MaxTitleLength = TmdbClient.MaxNameLength;

    // UPCitemdb's own codes, in every answer it sends (its Responses page).
    private const string OkCode = "OK";
    private const string NotFoundCode = "NOT_FOUND";
    private const string DailyLimitCode = "EXCEED_LIMIT";

    private const string RemainingHeader = "X-RateLimit-Remaining";

    private static readonly MediaTypeWithQualityHeaderValue Json = new(MediaTypeNames.Application.Json);

    private static readonly UpcAnswer NotFound = new(UpcStatus.NotFound, null, null, null);

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly ILogger<UpcItemDbClient> _logger;

    /// <summary>
    /// Gets a client ready. It sets the HttpClient's timeout and its cap on answers itself, so
    /// every UpcItemDbClient has them however it's made (SEC-5). The HttpClient must therefore be
    /// one that hasn't sent anything yet, as AddHttpClient gives each UpcItemDbClient. A plain
    /// constructor, since a primary one can't do this work (NET-10).
    /// </summary>
    public UpcItemDbClient(HttpClient http, IOptions<UpcItemDbOptions> options, ILogger<UpcItemDbClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        http.Timeout = LookupTimeout;
        http.MaxResponseContentBufferSize = MaxAnswerBytes;
        _http = http;
        _baseUrl = options.Value.BaseUrl;
        _logger = logger;
    }

    /// <summary>
    /// Looks a factory barcode up on UPCitemdb, sending it in Rewind's form, so a 13-digit EAN
    /// that starts with 0 goes as its 12-digit UPC-A. A code that isn't a factory barcode is not
    /// found, without asking UPCitemdb (SEC-5).
    /// </summary>
    /// <param name="upc">The barcode, such as 032429258373.</param>
    /// <param name="cancellationToken">Cancels the lookup. A cancelled lookup throws; it isn't UPCitemdb failing to answer.</param>
    public async Task<UpcAnswer> LookupAsync(string upc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upc);
        if (!Upc.TryNormalize(upc, out var code))
        {
            LogNotAUpc(_logger, upc.Length);
            return NotFound;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, $"lookup?upc={code}"));
        request.Headers.Accept.Add(Json);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var answer = await ReadAsync(response, cancellationToken);
            LogAnswer(code, answer, response);
            return answer;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or JsonException)
        {
            var reason = ReasonFor(e);
            LogNoAnswer(_logger, e, code, reason);
            return TryLater(reason);
        }
    }

    private static async Task<UpcAnswer> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var isJson = WebAnswers.IsJsonInUtf8(response.Content.Headers.ContentType);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK when !isJson:
                return TryLater(UpcTryLaterReason.Offline); // such as a Wi-Fi sign-in page
            case HttpStatusCode.OK:
                return FromReply(await response.Content.ReadFromJsonAsync<Reply>(cancellationToken));
            case HttpStatusCode.NotFound when isJson:
                // UPCitemdb's 404 also means a wrong address, and whatever sits between Rewind and
                // UPCitemdb can answer 404 too, so only its own NOT_FOUND is saved as not found.
                var notFound = await response.Content.ReadFromJsonAsync<Reply>(cancellationToken);
                return notFound?.Code == NotFoundCode ? NotFound : TryLater(UpcTryLaterReason.ServerError);
            case HttpStatusCode.TooManyRequests:
                var reason = await RefusalAsync(response, isJson, cancellationToken);
                return new UpcAnswer(UpcStatus.TryLater, null, reason, reason == UpcTryLaterReason.DailyLimit ? RetryAfterOf(response) : null);
            default: // such as a 404 that isn't UPCitemdb's, 400 INVALID_UPC, or a 5xx while UPCitemdb has trouble
                return TryLater(UpcTryLaterReason.ServerError);
        }
    }

    // Only UPCitemdb's own OK with a list of items is an answer, since "not found" is saved for a
    // month. Only the first item counts, as wall sheet R4 says, so a blank one isn't covered for by
    // another.
    private static UpcAnswer FromReply(Reply? reply)
    {
        if (reply is not { Code: OkCode, Items: { } items })
        {
            return TryLater(UpcTryLaterReason.ServerError); // JSON, but not UPCitemdb's answer to a lookup
        }

        return items is [{ Title: var title }, ..] && WebAnswers.Clean(title, MaxTitleLength) is { } trusted
            ? new UpcAnswer(UpcStatus.Found, trusted, null, null)
            : NotFound;
    }

    // A 429 means wait, whatever its body says, so the pacer's cooldown starts even when the body
    // can't be read. Only UPCitemdb's EXCEED_LIMIT says the day's lookups are used up; TOO_FAST,
    // HTTP_TOO_MANY_REQUESTS, and a 429 that doesn't say, such as one from the gateway in front of
    // UPCitemdb, all mean slow down.
    private static async Task<UpcTryLaterReason> RefusalAsync(HttpResponseMessage response, bool isJson, CancellationToken cancellationToken)
    {
        if (!isJson)
        {
            return UpcTryLaterReason.TooFast;
        }

        try
        {
            var refusal = await response.Content.ReadFromJsonAsync<Reply>(cancellationToken);
            return refusal?.Code == DailyLimitCode ? UpcTryLaterReason.DailyLimit : UpcTryLaterReason.TooFast;
        }
        catch (JsonException)
        {
            return UpcTryLaterReason.TooFast; // still a 429, so still a reason to wait; the warning logs it
        }
    }

    // SEC-5: .NET has parsed the header already. UPCitemdb sends a number of seconds once the day's
    // lookups are used up; a date, a value that won't parse, or no wait at all leaves the pacer to
    // choose.
    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } wait && wait > TimeSpan.Zero ? wait : null;

    // An answer over MaxAnswerBytes, or JSON that isn't UPCitemdb's, did arrive, with trouble. The
    // rest never arrived: no connection, or no answer within LookupTimeout.
    private static UpcTryLaterReason ReasonFor(Exception exception) =>
        exception is JsonException or HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded }
            ? UpcTryLaterReason.ServerError
            : UpcTryLaterReason.Offline;

    // OBS-3: each answer at Debug, with how many lookups UPCitemdb says are left, and each one that
    // isn't an answer at Warning. Nothing from the body is logged: only the status, the media type,
    // which .NET has parsed as a token, and the X-RateLimit-Remaining count, read as a number.
    private void LogAnswer(string upc, UpcAnswer answer, HttpResponseMessage response)
    {
        var remaining = RemainingOf(response);
        if (answer.Reason is { } reason)
        {
            LogRefused(_logger, upc, (int)response.StatusCode, response.Content.Headers.ContentType?.MediaType, reason, remaining);
            return;
        }

        LogAnswered(_logger, upc, answer.Status, remaining);
    }

    // A header is untrusted too, so only a plain whole number counts (SEC-5).
    private static int? RemainingOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(RemainingHeader, out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var remaining)
            ? remaining
            : null;

    private static UpcAnswer TryLater(UpcTryLaterReason reason) => new(UpcStatus.TryLater, null, reason, null);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Looked up {Upc} on UPCitemdb: {Status}, {Remaining} lookups left")]
    private static partial void LogAnswered(ILogger logger, string upc, UpcStatus status, int? remaining);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't look up {Upc} on UPCitemdb, which answered {HttpStatus} {MediaType}: {Reason}, {Remaining} lookups left. The clerk is asked to type the title")]
    private static partial void LogRefused(ILogger logger, string upc, int httpStatus, string? mediaType, UpcTryLaterReason reason, int? remaining);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't look up {Upc} on UPCitemdb: {Reason}. The clerk is asked to type the title")]
    private static partial void LogNoAnswer(ILogger logger, Exception exception, string upc, UpcTryLaterReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Didn't ask UPCitemdb about a code of {Length} characters that isn't a factory barcode")]
    private static partial void LogNotAUpc(ILogger logger, int length);

    // UPCitemdb's JSON, keeping only what Rewind reads. Every answer has a code, OK or an error's;
    // a lookup's also has items. Its names are lower case, which System.Net.Http.Json's web
    // defaults read without being told.
    private sealed record Reply(string? Code, List<Item>? Items);

    private sealed record Item(string? Title);
}
