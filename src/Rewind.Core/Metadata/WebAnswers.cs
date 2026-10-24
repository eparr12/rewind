using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;

namespace Rewind.Core.Metadata;

/// <summary>
/// What Rewind's web API clients do alike with the answers they read, which are all untrusted
/// (SEC-5): how big one may be, which ones are read at all, and how their text is kept.
/// <see cref="TmdbClient"/> uses them, and so will step 2.2's UPCitemdb client.
/// </summary>
internal static class WebAnswers
{
    /// <summary>
    /// The biggest answer Rewind reads. TMDB's details for a movie, with its whole cast and crew,
    /// come to tens of kilobytes, and a poster to about 30, so anything this big is wrong.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Whether an answer is JSON in UTF-8, as TMDB sends its answers, and so is read. A Wi-Fi
    /// sign-in page answers 200 OK too, in HTML, sometimes in a character set .NET can't decode, and
    /// reading that would throw an exception the clients don't expect. Media types and character
    /// sets are both case-insensitive.
    /// </summary>
    public static bool IsJsonInUtf8(MediaTypeHeaderValue? contentType) =>
        contentType is { MediaType: { } mediaType }
        && string.Equals(mediaType, MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase)
        && (contentType.CharSet is null || string.Equals(contentType.CharSet, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Text from an answer as Rewind keeps it: trimmed and capped, and blank text is unknown, so
    /// null (DATA-3). A cut never splits a character that takes two UTF-16 units, such as an emoji.
    /// </summary>
    public static string? Clean(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var cut = char.IsHighSurrogate(trimmed[maxLength - 1]) ? maxLength - 1 : maxLength;
        return trimmed[..cut].TrimEnd();
    }
}
