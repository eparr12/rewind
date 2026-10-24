using System.Globalization;
using System.Net;

namespace Rewind.Core.Tests;

/// <summary>
/// UPCitemdb's free plan, for tests: a <see cref="FakeWebApi"/> with UPCitemdb's address, whose
/// lookups carry its rate-limit headers, as the real one's do.
/// </summary>
internal sealed class FakeUpcItemDb : FakeWebApi
{
    /// <summary>How many lookups the answers say are left today, unless a test says otherwise.</summary>
    public const int Remaining = 99;

    /// <summary>Where UpcItemDbClient should look a barcode up, at UPCitemdb's own address.</summary>
    public static string LookupFor(string upc) => $"/prod/trial/lookup?upc={upc}";

    /// <summary>
    /// Answers a barcode's lookup with JSON, as UPCitemdb does, and with a Retry-After header when
    /// one is given, sent as it is, so a test can send one that doesn't parse.
    /// </summary>
    public void AnswerLookup(string upc, string json, HttpStatusCode status = HttpStatusCode.OK, int remaining = Remaining, string? retryAfter = null) =>
        Answer(LookupFor(upc), () =>
        {
            var response = WithLimits(Json(json, status), remaining);
            if (retryAfter is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            }

            return response;
        });

    /// <summary>
    /// Answers a barcode's lookup with JSON only once the test completes the task this returns, so
    /// the test can act while UPCitemdb is still answering.
    /// </summary>
    public TaskCompletionSource HoldLookup(string upc, string json)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AnswerLater(LookupFor(upc), async () =>
        {
            await release.Task;
            return WithLimits(Json(json, HttpStatusCode.OK), Remaining);
        });
        return release;
    }

    // The headers every answer from UPCitemdb carries. The reset time is a Unix time.
    private static HttpResponseMessage WithLimits(HttpResponseMessage response, int remaining)
    {
        response.Headers.Add("X-RateLimit-Limit", "100");
        response.Headers.Add("X-RateLimit-Remaining", remaining.ToString(CultureInfo.InvariantCulture));
        response.Headers.Add("X-RateLimit-Reset", "1792939952");
        return response;
    }
}
