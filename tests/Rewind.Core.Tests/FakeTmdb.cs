using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Rewind.Core.Tests;

/// <summary>
/// TMDB's API and image server, for tests (TEST-5): each request gets the answer set up for its
/// path and query, or 404 Not Found, and is recorded. Nothing goes over the network.
/// </summary>
internal sealed class FakeTmdb : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);
    private readonly List<SentRequest> _requests = [];

    /// <summary>Every request so far, oldest first.</summary>
    public IReadOnlyList<SentRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Where TmdbClient should search for a title, already escaped as a URL's query.</summary>
    public static string SearchFor(string escapedTitle) => $"/3/search/movie?query={escapedTitle}&include_adult=false";

    /// <summary>Where TmdbClient should ask for a movie's details.</summary>
    public static string MovieFor(int tmdbId) =>
        string.Create(CultureInfo.InvariantCulture, $"/3/movie/{tmdbId}?append_to_response=credits,release_dates");

    /// <summary>Where TmdbClient should download a poster at a width, such as w342.</summary>
    public static string PosterFor(string width, string posterPath) => $"/t/p/{width}{posterPath}";

    public void AnswerJson(string pathAndQuery, string json) =>
        _answers[pathAndQuery] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    public void AnswerJpeg(string path, byte[] jpeg) =>
        _answers[path] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(jpeg) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } },
        };

    /// <summary>Answers 200 OK with a web page, as a Wi-Fi sign-in page does.</summary>
    public void AnswerHtml(string pathAndQuery) =>
        _answers[pathAndQuery] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Sign in to the Wi-Fi</body></html>", Encoding.UTF8, "text/html"),
        };

    /// <summary>Answers 200 OK with this body, sent as this Content-Type, such as "application/json; charset=windows-1252".</summary>
    public void AnswerAs(string pathAndQuery, string body, string contentType) =>
        _answers[pathAndQuery] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
        };

    public void AnswerStatus(string pathAndQuery, HttpStatusCode status) =>
        _answers[pathAndQuery] = () => new HttpResponseMessage(status);

    /// <summary>Throws instead of answering, as HttpClient does when the register is offline or TMDB is slow.</summary>
    public void Throw(string pathAndQuery, Exception exception) =>
        _answers[pathAndQuery] = () => throw exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // HttpClient always gives the request its URI before a handler sees it.
        var uri = request.RequestUri!;
        lock (_requests)
        {
            _requests.Add(new SentRequest(uri, request.Headers.Authorization?.ToString()));
        }

        return Task.FromResult(_answers.TryGetValue(uri.PathAndQuery, out var answer)
            ? answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>One request TMDB received.</summary>
    /// <param name="Uri">Where it went.</param>
    /// <param name="Authorization">Its Authorization header, or null when it had none.</param>
    public sealed record SentRequest(Uri Uri, string? Authorization);
}
