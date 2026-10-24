using System.Text;
using System.Text.RegularExpressions;

namespace Rewind.Core.Metadata;

/// <summary>
/// Turns a product's title from UPCitemdb into the movie's title to search TMDB with, such as
/// "Halloween (Widescreen DVD)" into "Halloween" (Build Plan 2.2).
/// </summary>
public static partial class ProductTitles
{
    // Such as "30th ", before a kind of edition. [0-9], never \d, which matches every script's digits.
    private const string Ordinal = "(?:[0-9]+(?:st|nd|rd|th)\\s+)";

    // What a DVD's product title adds to the movie's. BarcodeTitles cleans each title as it reads
    // it, from the product's title as UPCitemdb gave it, so a word added here cleans the answers
    // already saved too. Each goes only as a whole word or phrase, in any case, with any "Edition",
    // "Version" or "Cut" after it, so "Full Metal Jacket" keeps its "Full" and "Toy Story 2" its 2.
    private static readonly string[] EditionWords =
    [
        "DVD",
        "Blu-ray",
        "Widescreen",
        "Full Screen",
        "Fullscreen",
        "Two-Disc",
        "2-Disc",
        "Single Disc",
        "Unrated",
        "Remastered",
        "Director's Cut",
        "Extended",
        "Theatrical",
        "Digital Copy",
        "Combo",
        "4K",
        "UHD",
        "Ultra HD",
    ];

    // Kinds of edition, which go only before "Edition", one or more of them, with an ordinal such
    // as "30th" in front: alone they can be part of a title, as in "The Special".
    private static readonly string[] EditionKinds =
    [
        "Special",
        "Collector's",
        "Anniversary",
        "Platinum",
        "Deluxe",
        "Limited",
        "Ultimate",
        "Signature",
        "Definitive",
    ];

    // What's left around the title once the rest has gone, such as the " - " in
    // "Toy Story 2 - Special Edition DVD". The ones right around a word that goes, go with it, so
    // "Jaws Blu-ray/DVD Combo" leaves "Jaws".
    private static readonly char[] Separators = [' ', '-', ':', ',', '/', '+', '&'];

    // An edition word, with what follows it; kinds of edition before "Edition"; or an anniversary
    // such as "20th Anniversary" on its own. Separators right around it go with it.
    private static readonly Regex EditionPattern = new(
        $@"[\s\-:,/+&]*\b(?:{Words(EditionWords)}(?:\s+(?:Edition|Version|Cut))?|{Ordinal}?{Words(EditionKinds)}(?:\s+{Words(EditionKinds)})*\s+Edition|{Ordinal}Anniversary)\b[\s\-:,/+&]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Cleans a product's title: drops anything in brackets, (), [] or {}, with the brackets, and
    /// everything after a bracket that never closes; drops words such as DVD, Widescreen and
    /// Special Edition; collapses the spaces and trims separators such as " - " from both ends; and
    /// puts a product list's "Matrix, The" back as "The Matrix". The case of what's left never
    /// changes.
    /// </summary>
    /// <returns>
    /// The movie's title, at most <see cref="UpcItemDbClient.MaxTitleLength"/> characters (SEC-5).
    /// Null when nothing is left, or there was nothing to begin with (DATA-3).
    /// </returns>
    public static string? Clean(string? productTitle)
    {
        // Capped first, so the patterns below never see more than a title's worth (SEC-5). The rest
        // only ever takes characters away or moves them, so the movie's title is within the cap too.
        if (WebAnswers.Clean(productTitle, UpcItemDbClient.MaxTitleLength) is not { } title)
        {
            return null;
        }

        var words = EditionPattern.Replace(WithoutBrackets(title), " ");
        var movieTitle = ArticleFirst(Spaces().Replace(words, " ").Trim(Separators));
        return movieTitle.Length == 0 ? null : movieTitle;
    }

    // Each part in brackets becomes a space, so the words either side stay apart. A closing
    // bracket that never opened goes on its own.
    private static string WithoutBrackets(string title)
    {
        var kept = new StringBuilder(title.Length);
        var depth = 0;
        foreach (var c in title)
        {
            if (c is '(' or '[' or '{')
            {
                if (depth == 0)
                {
                    kept.Append(' ');
                }

                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth = Math.Max(depth - 1, 0);
            }
            else if (depth == 0)
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    // Product lists file "The Matrix" as "Matrix, The", and TMDB knows it by its name.
    private static string ArticleFirst(string title)
    {
        var match = TrailingArticle().Match(title);
        return match.Success ? $"{match.Groups["article"].Value} {match.Groups["rest"].Value}" : title;
    }

    // One alternative for each phrase. A space or a hyphen in a phrase matches either, or a run of
    // them, and an apostrophe matches a straight or a curly one.
    private static string Words(string[] phrases) =>
        "(?:" + string.Join('|', phrases.Select(phrase => string.Join(
            @"[\s\-]+",
            phrase.Split([' ', '-']).Select(part => Regex.Escape(part).Replace("'", "['’]", StringComparison.Ordinal))))) + ")";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?<rest>.+),\s*(?<article>The|A|An)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingArticle();
}
