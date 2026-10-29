using System.Text;

namespace Rewind.Core.Concessions;

/// <summary>
/// The barcode a snack has on the concession menu: CON- and 1 to 20 capital letters, the form
/// Barcodes.Classify reads as a concession (Build Plan 3.1).
/// </summary>
public static class ConcessionCodes
{
    /// <summary>What every concession's code starts with.</summary>
    public const string Prefix = "CON-";

    /// <summary>The most letters a code has after <see cref="Prefix"/>.</summary>
    public const int MaxLetters = 20;

    /// <summary>The longest code: <see cref="Prefix"/> and <see cref="MaxLetters"/> letters.</summary>
    public static int MaxLength => Prefix.Length + MaxLetters;

    /// <summary>
    /// A code made from a snack's name, for the Store Manager to keep or fix before the snack is saved: the
    /// name's letters in capitals, with their accents dropped, cut at <see cref="MaxLetters"/>. So Popcorn is
    /// CON-POPCORN, and Jalapeño poppers is CON-JALAPENOPOPPERS. A name with no letters gives the prefix
    /// alone, for the Store Manager to finish.
    /// </summary>
    public static string Suggest(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // SEC-5: read rune by rune first, so half a surrogate pair from a bad paste becomes U+FFFD, as
        // WebAnswers reads typed text, rather than make Normalize throw on a keystroke.
        var whole = string.Concat(name.EnumerateRunes().Select(rune => rune.ToString()));

        // Decomposed, an accented letter is its plain letter followed by a mark, so ñ keeps its n.
        var letters = new StringBuilder(MaxLetters);
        foreach (var c in whole.Normalize(NormalizationForm.FormD).ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z')
            {
                letters.Append(c);
                if (letters.Length == MaxLetters)
                {
                    break;
                }
            }
        }

        return Prefix + letters;
    }
}
