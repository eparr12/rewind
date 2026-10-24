using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;

namespace Rewind.Core.Metadata;

/// <summary>
/// What Rewind's web API clients, <see cref="TmdbClient"/> and <see cref="UpcItemDbClient"/>, do
/// alike with the answers they read, which are all untrusted (SEC-5): how big one may be, which ones
/// are read at all, and how their text is kept.
/// </summary>
internal static class WebAnswers
{
    /// <summary>
    /// The biggest answer Rewind reads from either. TMDB's details for a movie, with its whole cast
    /// and crew, come to tens of kilobytes, a poster to about 30, and UPCitemdb's answer for a
    /// barcode to about 5, so anything this big is wrong.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Whether an answer is JSON in UTF-8, as TMDB and UPCitemdb send theirs, and so is read. A
    /// Wi-Fi sign-in page answers 200 OK too, in HTML, sometimes in a character set .NET can't
    /// decode, and reading that would throw an exception the clients don't expect. Media types and
    /// character sets are both case-insensitive.
    /// </summary>
    public static bool IsJsonInUtf8(MediaTypeHeaderValue? contentType) =>
        contentType is { MediaType: { } mediaType }
        && string.Equals(mediaType, MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase)
        && (contentType.CharSet is null || string.Equals(contentType.CharSet, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase));

    private const char ZeroWidthNonJoiner = '\u200C';
    private const char ZeroWidthJoiner = '\u200D';

    /// <summary>
    /// Text from an answer as Rewind keeps it. A control character, such as a line break, or a
    /// line or paragraph separator becomes a space, so the words either side stay apart. An
    /// invisible format character, such as a right-to-left override or a zero-width space, goes,
    /// so the text can't be made to look like something it isn't; but not a zero-width joiner or
    /// non-joiner, which writing such as Persian and emoji such as a family need to show as meant.
    /// What's left is trimmed and capped, and blank text is unknown, so null (DATA-3). A cut never
    /// splits a character that takes two UTF-16 units, such as an emoji.
    /// </summary>
    public static string? Clean(string? text, int maxLength)
    {
        if (text is null)
        {
            return null;
        }

        var visible = Trimmed(Visible(text));
        if (visible.Length == 0)
        {
            return null;
        }

        if (visible.Length <= maxLength)
        {
            return visible;
        }

        var cut = char.IsHighSurrogate(visible[maxLength - 1]) ? maxLength - 1 : maxLength;
        return Trimmed(visible[..cut]);
    }

    // Character by character as Unicode counts them, so a format character beyond the first 65,536,
    // such as a tag character, is caught too. A broken surrogate pair becomes U+FFFD, the
    // replacement character, as Rune reads it.
    private static string Visible(string text)
    {
        var visible = new StringBuilder(text.Length);
        Span<char> units = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
                    visible.Append(' ');
                    break;
                case UnicodeCategory.Format when rune.Value is not (ZeroWidthNonJoiner or ZeroWidthJoiner):
                    break;
                default:
                    visible.Append(units[..rune.EncodeToUtf16(units)]);
                    break;
            }
        }

        return visible.ToString();
    }

    // Trimmed of white space, and of joiners too, which join nothing at either end, so text of
    // nothing else is blank.
    private static string Trimmed(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsBlank(text[start]))
        {
            start++;
        }

        while (end > start && IsBlank(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static bool IsBlank(char c) => char.IsWhiteSpace(c) || c is ZeroWidthNonJoiner or ZeroWidthJoiner;
}
