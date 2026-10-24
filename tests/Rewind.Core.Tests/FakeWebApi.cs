using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Rewind.Core.Tests;

/// <summary>
/// A web API for tests, such as TMDB's or UPCitemdb's (TEST-5): each request gets the answer set up
/// for its path and query, or 404 Not Found, and is recorded. Nothing goes over the network.
/// </summary>
internal class FakeWebApi : HttpMessageHandler
{
    private readonly Dictionary<string, Func<Task<HttpResponseMessage>>> _answers = new(StringComparer.Ordinal);
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

    public void AnswerJson(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Answer(pathAndQuery, () => Json(json, status));

    public void AnswerJpeg(string path, byte[] jpeg) =>
        Answer(path, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(jpeg) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } },
        });

    /// <summary>Answers 200 OK with a web page, as a Wi-Fi sign-in page does.</summary>
    public void AnswerHtml(string pathAndQuery) =>
        Answer(pathAndQuery, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Sign in to the Wi-Fi</body></html>", Encoding.UTF8, "text/html"),
        });

    /// <summary>Answers with this body, sent as this Content-Type, such as "application/json; charset=windows-1252".</summary>
    public void AnswerAs(string pathAndQuery, string body, string contentType, HttpStatusCode status = HttpStatusCode.OK) =>
        Answer(pathAndQuery, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
        });

    public void AnswerStatus(string pathAndQuery, HttpStatusCode status) =>
        Answer(pathAndQuery, () => new HttpResponseMessage(status));

    /// <summary>Throws instead of answering, as HttpClient does when the register is offline or the API is slow.</summary>
    public void Throw(string pathAndQuery, Exception exception) =>
        _answers[pathAndQuery] = () => throw exception;

    /// <summary>Answers with what the task gives, once it does, such as an answer the test holds back.</summary>
    public void AnswerLater(string pathAndQuery, Func<Task<HttpResponseMessage>> answer) =>
        _answers[pathAndQuery] = answer;

    protected static HttpResponseMessage Json(string json, HttpStatusCode status) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected void Answer(string pathAndQuery, Func<HttpResponseMessage> answer) =>
        _answers[pathAndQuery] = () => Task.FromResult(answer());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // HttpClient always gives the request its URI before a handler sees it.
        var uri = request.RequestUri!;
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (_requests)
        {
            _requests.Add(new SentRequest(uri, headers));
        }

        return _answers.TryGetValue(uri.PathAndQuery, out var answer)
            ? await answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>One request the API received.</summary>
    /// <param name="Uri">Where it went.</param>
    /// <param name="Headers">Its headers, by name, each with its values joined by commas.</param>
    public sealed record SentRequest(Uri Uri, IReadOnlyDictionary<string, string> Headers)
    {
        /// <summary>Its Authorization header, or null when it had none.</summary>
        public string? Authorization => Headers.GetValueOrDefault("Authorization");
    }
}
